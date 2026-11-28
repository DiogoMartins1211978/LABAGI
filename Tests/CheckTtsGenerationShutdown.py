"""Exercise actual pool generation/shutdown methods with a controlled native stand-in."""
from pathlib import Path
import json, re, shutil, subprocess, tempfile
root = Path(__file__).resolve().parent.parent
source = (root / 'Packages/com.ponyudev.sherpa-onnx/Runtime/Tts/Engine/TtsEngine.cs').read_text(encoding='utf-8-sig')
def method(signature):
    start = source.index(signature)
    brace = source.index('{', start)
    depth, end = 1, brace + 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]
harness = r'''
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
class OfflineTts : IDisposable { public int Disposals; public void Dispose() => Interlocked.Increment(ref Disposals); }
class TtsResult { }
static class EngineRegistry { public static void Unregister(object o) {} }
static class SherpaOnnxLog { public static void RuntimeLog(string s) {} }
class Engine {
    readonly ConcurrentQueue<OfflineTts> _available = new();
    readonly ReaderWriterLockSlim _lifecycleLock = new();
    OfflineTts[] _pool;
    SemaphoreSlim _semaphore;
    int _poolSize = 1, _disposed;
    public int SampleRate { get; private set; }
    public int NumSpeakers { get; private set; }
    public bool IsLoaded => _pool != null && _pool.Length > 0;
    public readonly OfflineTts Native = new();
    public Engine() { _pool = new[] {Native}; _available.Enqueue(Native); _semaphore = new(1,1); }
    public Task<TtsResult> Start(Func<OfflineTts,TtsResult> work, CancellationToken ct = default) => RentAndGenerateAsync(work,ct);
    @METHODS@
}
class Program {
    static int checks;
    static void Check(bool pass, string why) { if(!pass) throw new Exception(why); checks++; }
    static async Task Main() {
        var engine = new Engine();
        var entered = new ManualResetEventSlim(); var release = new ManualResetEventSlim();
        var generation = engine.Start(native => {
            entered.Set(); release.Wait();
            Check(native.Disposals == 0, "Native engine freed during generation");
            return new TtsResult();
        });
        Check(entered.Wait(3000), "Generation did not start");
        using var cancellation = new CancellationTokenSource();
        var queued = engine.Start(native => throw new Exception("Cancelled work entered native engine"), cancellation.Token);
        cancellation.Cancel();
        try { await queued.WaitAsync(TimeSpan.FromSeconds(3)); throw new Exception("Expected cancellation"); }
        catch(OperationCanceledException) { checks++; }
        var disposing = new ManualResetEventSlim();
        var shutdown = Task.Run(() => { disposing.Set(); engine.Dispose(); });
        Check(disposing.Wait(3000), "Shutdown worker did not start");
        try {
            await Task.Delay(100);
            Check(engine.Native.Disposals == 0 && !shutdown.IsCompleted, "Shutdown must wait for native generation");
        } finally { release.Set(); }
        await generation.WaitAsync(TimeSpan.FromSeconds(3));
        await shutdown.WaitAsync(TimeSpan.FromSeconds(3));
        Check(engine.Native.Disposals == 1, "Native engine should be disposed exactly once");
        engine.Dispose();
        Check(engine.Native.Disposals == 1, "Repeated disposal must be harmless");
        var late = await engine.Start(native => throw new Exception("Native work ran after shutdown"));
        Check(late == null, "Late worker should not access disposed native pool");
        Console.WriteLine("PASS: " + checks + " generation/shutdown checks (controlled native engine).");
    }
}
'''.replace('@METHODS@', '\n'.join(method(s) for s in ['private Task<TtsResult> RentAndGenerateAsync(', 'public void Unload()', 'public void Dispose()']))
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
