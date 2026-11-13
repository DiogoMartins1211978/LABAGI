using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Tools.Events
{
    public sealed class MetricsRecorder : MonoBehaviour
    {
        // --- DATA TYPES ---

        [Serializable]
        public sealed class MetricBinding
        {
            public MetricChannel channel;
            public string eventName;
        }

        [Serializable]
        public sealed class SignalBinding
        {
            public SignalChannel channel;
            public string eventName;
        }

        [Serializable]
        public sealed class Record
        {
            public int schemaVersion = 1;
            public string eventKind = "metric";
            public long sequence;
            public string sessionId, runId, gameVersion, utc, eventName;
            public double elapsedSeconds;
            public string subjectId, decisionId, context, outcome;
            public bool hasValue;
            public double value;
        }

        sealed class Decision
        {
            public string context;
            public double started, pausedAt, excluded;
            public bool paused;
        }

        // --- CONFIGURABLE FIELDS ---

        [SerializeField]
        private MetricBinding[] metrics = Array.Empty<MetricBinding>();
        [SerializeField]
        private SignalBinding[] signals = Array.Empty<SignalBinding>();
        [Header("File Writing")]
        [SerializeField, Min(0.1f)]
        private float flushInterval = 2;

        // --- RECORDING STATE ---

        readonly List<Action> unsubscribe = new List<Action>();
        readonly Dictionary<string, Decision> decisions = new Dictionary<string, Decision>();
        StreamWriter writer;
        double started, lastFlush;
        long sequence;
        string sessionId, runId = "";
        public string OutputPath { get; private set; }
        public bool IsRecording => writer != null;

        void OnEnable()
        {
            // Each enabled recording period gets its own file
            sessionId = Guid.NewGuid().ToString("N");
            sequence = 0;
            runId = "";
            started = lastFlush = Time.realtimeSinceStartupAsDouble;
            try
            {
                string directory = Path.Combine(Application.persistentDataPath, "Metrics");
                Directory.CreateDirectory(directory);
                OutputPath = Path.Combine(directory, sessionId + ".jsonl");
                // Allow analysis tools to read the session while the recorder is running.
                writer = new StreamWriter(new FileStream(OutputPath, FileMode.CreateNew,
                    FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
                // Keep the removal actions so every subscription can be released
                foreach (var binding in metrics)
                {
                    if (binding == null || binding.channel == null || string.IsNullOrWhiteSpace(binding.eventName)) continue;
                    var channel = binding.channel;
                    string name = binding.eventName;
                    Action<MetricEvent> listener = data => Track(name, data);
                    channel.Raised += listener;
                    unsubscribe.Add(() => channel.Raised -= listener);
                }
                foreach (var binding in signals)
                {
                    if (binding == null || binding.channel == null || string.IsNullOrWhiteSpace(binding.eventName)) continue;
                    var channel = binding.channel;
                    string name = binding.eventName;
                    Action listener = () => Write(new Record { eventName = name, eventKind = "signal" });
                    channel.Raised += listener;
                    unsubscribe.Add(() => channel.Raised -= listener);
                }
                Track("session.started", new MetricEvent(""));
                Flush();
            }
            catch (Exception error) { Fail(error); }
        }

        // --- RUN LIFECYCLE ---

        public void StartRun(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A run ID is required.", nameof(id));
            // Repeated setup calls must not close the current attempt or its decisions.
            if (runId == id) return;
            // Finish the previous attempt before assigning the new identity.
            if (!string.IsNullOrEmpty(runId)) EndRun();
            runId = id;
            Track("run.started", new MetricEvent(""));
        }

        public void EndRun()
        {
            CancelPending();
            if (!string.IsNullOrEmpty(runId)) Track("run.ended", new MetricEvent(""));
            runId = "";
            Flush();
        }

        // --- CUSTOM METRICS ---

        public void Track(string eventName, MetricEvent data)
        {
            if (string.IsNullOrWhiteSpace(eventName)) throw new ArgumentException("An event name is required.");
            if (data.HasValue && (double.IsNaN(data.Value) || double.IsInfinity(data.Value)))
                throw new ArgumentException("Metric values must be finite.");
            Write(new Record { eventName = eventName,
                subjectId = data.SubjectId ?? "", hasValue = data.HasValue, value = data.Value });
        }

        // --- DECISION TIMING ---

        // Start when the choices become usable, after transitions and animations
        public string BeginDecision(string context)
        {
            if (string.IsNullOrWhiteSpace(context)) throw new ArgumentException("A decision context is required.");
            if (!IsRecording) throw new InvalidOperationException("Start recording before timing a decision.");
            string id = Guid.NewGuid().ToString("N");
            decisions.Add(id, new Decision { context = context,
                started = Time.realtimeSinceStartupAsDouble });
            Write(new Record { eventName = "decision.started", decisionId = id,
                context = context });
            return id;
        }

        public void PauseDecision(string id)
        {
            // Repeated pause calls must not restart the excluded interval
            if (!decisions.TryGetValue(id, out var decision) || decision.paused) return;
            decision.paused = true;
            decision.pausedAt = Time.realtimeSinceStartupAsDouble;
        }

        public void ResumeDecision(string id)
        {
            if (!decisions.TryGetValue(id, out var decision) || !decision.paused) return;
            // Accumulate excluded time without changing the original start time.
            decision.excluded += Time.realtimeSinceStartupAsDouble - decision.pausedAt;
            decision.paused = false;
        }

        public bool EndDecision(string id, string selectedId = "", bool cancelled = false)
        {
            // Removing the ID prevents the same choice from being counted twice
            if (!decisions.TryGetValue(id, out var decision)) return false;
            // If still paused, stop at the pause boundary instead of including idle time.
            double end = decision.paused ? decision.pausedAt : Time.realtimeSinceStartupAsDouble;
            decisions.Remove(id);
            Write(new Record { eventName = "decision.ended", decisionId = id,
                context = decision.context, subjectId = selectedId,
                outcome = cancelled ? "cancelled" : "confirmed", hasValue = true,
                value = Math.Max(0, end - decision.started - decision.excluded) });
            return true;
        }

        void CancelPending()
        {
            foreach (string id in new List<string>(decisions.Keys)) EndDecision(id, cancelled: true);
        }

        // --- FILE OUTPUT ---

        void Write(Record record)
        {
            if (writer == null) return;
            // Attach shared metadata here so publishers only supply event-specific data.
            record.sequence = ++sequence;
            record.sessionId = sessionId;
            record.runId = runId;
            record.gameVersion = Application.version;
            record.utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            record.elapsedSeconds = Time.realtimeSinceStartupAsDouble - started;
            try { writer.WriteLine(JsonUtility.ToJson(record)); }
            catch (Exception error) { Fail(error); }
        }

        void Update()
        {
            // Flush periodically instead of flushing every individual event
            if (Time.realtimeSinceStartupAsDouble - lastFlush >= Math.Max(0.1, flushInterval)) Flush();
        }

        public void Flush()
        {
            lastFlush = Time.realtimeSinceStartupAsDouble;
            try { writer?.Flush(); }
            catch (Exception error) { Fail(error); }
        }

        // --- CLEANUP ---

        void OnApplicationPause(bool paused) { if (paused) Flush(); }
        void OnApplicationQuit() => Flush();

        void OnDisable()
        {
            // Finish pending decisions before closing the session
            foreach (var remove in unsubscribe) remove();
            unsubscribe.Clear();
            EndRun();
            Track("session.ended", new MetricEvent(""));
            Flush();
            try { writer?.Dispose(); }
            catch (Exception error) { Debug.LogException(error, this); }
            writer = null;
        }

        void Fail(Exception error)
        {
            Debug.LogError("Metrics recording stopped: " + error.Message, this);
            try { writer?.Dispose(); } catch { /* Preserve the original error. */ }
            // Disable further writes after a disk error; gameplay can continue.
            writer = null;
        }

        [ContextMenu("Print metrics file path")]
        void PrintPath() => Debug.Log(OutputPath ?? "Start Play Mode to create a session.", this);
    }
}
