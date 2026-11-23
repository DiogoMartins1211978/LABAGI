"""Run the production Awake/OnDestroy bodies with controllable initialization.

Uses fake service/native lifetimes; the final speech/scene round trip is tested in Unity.
Requires the .NET SDK, and does not read or change real speech or save data.
"""
from pathlib import Path
import json
import re
import shutil
import subprocess
import tempfile

root = Path(__file__).resolve().parent.parent
source = (root / 'Packages/com.ponyudev.sherpa-onnx/Runtime/Tts/MonoBehaviours/TtsOrchestrator.cs').read_text(encoding='utf-8-sig')

def method(signature):
    start = source.index(signature)
    brace = source.index('{', start)
    depth = 1
    end = brace + 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]

harness = r'''
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
class TtsService {
    public static TtsService Last;
    public TaskCompletionSource<bool> Completion = new();
    public bool Loading = true;
    public int Disposals;
    public bool DisposedDuringLoad;
    public TtsService() { Last = this; }
    public Task InitializeAsync() => Completion.Task;
    public Settings Settings { get { if (Disposals > 0) throw new Exception("Disposed service accessed"); return new Settings(); } }
    public void Finish(Exception error = null) { Loading = false; if (error == null) Completion.SetResult(true); else Completion.SetException(error); }
    public void Dispose() { DisposedDuringLoad |= Loading; Disposals++; }
}
class Settings { public object cache = new object(); }
class CachedTtsService {
    TtsService service;
    public CachedTtsService(TtsService service, object cache, object transform) { this.service = service; }
    public void Dispose() { service.Dispose(); }
}
class Handle { public void Dispose() { } }
static class Debug { public static int Errors; public static void LogException(Exception e, object context) { Errors++; } }
class Orchestrator {
    TtsService _innerService;
    CachedTtsService _cachedService;
    bool _initializeOnAwake = true, _initializing, _destroyed;
    List<Handle> _activeHandles = new();
    object transform { get { if (_destroyed) throw new Exception("Destroyed transform accessed"); return this; } }
    public bool IsInitialized { get; private set; }
    public event Action Initialized;
    public void Start() => Awake();
    public void Destroy() => OnDestroy();
    @METHODS@
}
static class Checks {
    static int count;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); count++; }
    static async Task Main() {
        var old = new Orchestrator(); int oldReady = 0; old.Initialized += () => oldReady++;
        old.Start(); var abandoned = TtsService.Last; old.Destroy();
        Check(abandoned.Disposals == 0, "Destroy must defer disposal until native initialization completes");
        var next = new Orchestrator(); int nextReady = 0; next.Initialized += () => nextReady++;
        next.Start(); var current = TtsService.Last;
        abandoned.Finish(); await Task.Delay(10);
        Check(abandoned.Disposals == 1 && !abandoned.DisposedDuringLoad, "Abandoned service must be disposed once, after loading");
        Check(oldReady == 0 && !old.IsInitialized && Debug.Errors == 0, "Destroyed component must never announce Ready or access disposed state");
        current.Finish(); await Task.Delay(10);
        Check(nextReady == 1 && next.IsInitialized && current.Disposals == 0, "The replacement scene's service must initialize normally");
        next.Destroy(); Check(current.Disposals == 1 && !next.IsInitialized, "Live service must dispose on scene exit");
        var failed = new Orchestrator(); failed.Start(); var failing = TtsService.Last;
        failing.Finish(new Exception("real initialization failure")); await Task.Delay(10);
        Check(Debug.Errors == 1 && !failed.IsInitialized, "Genuine startup errors must remain visible");
        failed.Destroy(); Check(failing.Disposals == 1, "Failed initialization must still clean up");
        var cancelled = new Orchestrator(); cancelled.Start(); var cancelling = TtsService.Last;
        cancelled.Destroy(); cancelling.Finish(new OperationCanceledException()); await Task.Delay(10);
        Check(cancelling.Disposals == 1 && Debug.Errors == 1, "Shutdown cancellation must clean up without a spurious error");
        var immediate = new Orchestrator(); immediate.Initialized += immediate.Destroy;
        immediate.Start(); var immediateService = TtsService.Last; immediateService.Finish(); await Task.Delay(10);
        Check(immediateService.Disposals == 1 && !immediate.IsInitialized, "Scene exit from a Ready callback must not double-dispose");
        Console.WriteLine("PASS: " + count + " TTS lifecycle checks (controlled service).");
    }
}
'''.replace('@METHODS@', method('private async void Awake()') + '\n' + method('private void OnDestroy()'))

dotnet = Path(shutil.which('dotnet')).resolve()
def version_key(name):
    return tuple(int(part) for part in re.findall(r'\d+', name))
sdk = max((dotnet.parent / 'sdk').glob('*/Roslyn/bincore/csc.dll'), key=lambda p: version_key(p.parents[2].name))
refs = max((dotnet.parent / 'packs/Microsoft.NETCore.App.Ref').glob('*/ref/net*'), key=lambda p: version_key(p.name))
major = refs.name.removeprefix('net').split('.')[0]
runtime = max((dotnet.parent / 'shared/Microsoft.NETCore.App').glob(major + '.*'), key=lambda p: version_key(p.name)).name
temp = Path(tempfile.mkdtemp(prefix='labagi-tts-check-'))
cs = temp / 'Checks.cs'
cs.write_text(harness)
out = temp / 'Checks.dll'
rsp = temp / 'Checks.rsp'
rsp.write_text('\n'.join(['-nologo', '-target:exe', f'-out:"{out}"'] + [f'-r:"{p}"' for p in refs.glob('*.dll')] + [f'"{cs}"']))
subprocess.run([str(dotnet), str(sdk), '@' + str(rsp)], check=True)
out.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions': {'tfm': refs.name, 'framework': {'name': 'Microsoft.NETCore.App', 'version': runtime}}}))
subprocess.run([str(dotnet), str(out)], check=True)
