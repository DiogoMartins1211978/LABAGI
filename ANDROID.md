# Android

Android configuration belongs to the whole EchoesBelow project, including rendering, speech and location. Tool-specific behaviour is documented in the [Georeferencing Guide](Assets/Tools/Georeferencing/README.md).

## Configuration In The Repository

- The Android build profile is in `Assets/Settings/Build Profiles`, uses the shared scene list and has **Development Build** enabled.
- Player Settings use IL2CPP, ARM64, minimum API 26, Automatic target API selection and the GameActivity entry point.
- `Assets/Plugins/Android/AndroidManifest.xml` declares microphone, coarse location and fine location permissions. GPS hardware is optional, so devices without GPS can install the app. Runtime permission requests remain the responsibility of the relevant feature.
- Sherpa-ONNX is configured at version 1.13.0 with `SHERPA_ONNX` enabled for Android. Its four ARM64 native libraries are included under `Assets/Plugins/SherpaOnnx/Android/arm64-v8a`, with import settings restricted to Android ARM64. They come from the [official 1.13.0 Android release](https://github.com/k2-fsa/sherpa-onnx/releases/tag/v1.13.0).
- `Builds/` and generated APKs are excluded from version control.

These settings and libraries are shared through Git; developers do not need to recreate them or reinstall Sherpa's Android libraries after checkout. The application identifier currently remains the Unity template identifier in Player Settings; changing it changes the app's identity and local data location.

## Local Setup

Each machine needs **Android Build Support**, **Android SDK & NDK Tools** and **OpenJDK** installed through Unity Hub for the Editor version in `ProjectSettings/ProjectVersion.txt`. Use the Unity-installed tools in **Edit > Preferences > External Tools > Android**.

Native libraries and speech models use Git LFS. If a checkout contains small text pointers instead of the binaries, install Git LFS and run `git lfs pull` from the project root before building.

Enable Developer Options and USB debugging on the phone. Connect it, unlock it and accept the computer's USB debugging authorization prompt. With Android's `platform-tools` on PATH, check the connection:

```powershell
adb devices -l
```

`device` means the phone is ready. `unauthorized` means the authorization prompt still needs accepting. Unity's bundled ADB is available under the Editor's `Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools` directory if it is not on PATH.

## Build And Run

1. Open **File > Build Profiles**, select the existing Android profile and choose **Switch Profile** if it is not active.
2. Select the connected phone under **Run Device**. Development Build is already enabled in the shared profile.
3. For a complete installation, select regular **Build And Run** under **Build To Device** and save the APK under `Builds/Android/`. Use **Patch And Run** for subsequent development iterations. The first build takes longer while Unity imports assets and compiles native code.

An APK can also be installed manually:

```powershell
adb install -r Builds/Android/EchoesBelow.apk
adb logcat -s Unity sherpa-onnx AndroidRuntime libc DEBUG
```

Use the APK's actual filename. Installing with `-r` preserves app data when the application identifier and signing key are unchanged. Development APKs use Unity's debug signing; release signing is a separate distribution setup.

## Diagnose And Iterate

Android Logcat 1.4.7 is included as a project dependency. Open **Tools > Android Logs** or **Window > Analysis > Android Logcat**. No separate package installation is needed after checkout.

1. Connect and authorize the USB device, then select it and the game's application in the Logcat window. An app that is not running may require **All** until its process appears.
2. Start capture before launching the installed game. Keep the minimum priority at **Info** or **Verbose** to include `Debug.Log` messages.
3. Search for `[Location]` to follow startup acquisition in updated builds. Older builds use `Startup Location:` or `Startup location unavailable:`. Remove the search to inspect other systems; native Sherpa messages use the `sherpa-onnx` tag.
4. Export the messages to `Logs/Android/` when sharing a reproduction. The window's export controls choose which captured messages to save; opening the window does not create a project log file automatically.

Location requires both an app permission and Android's device-wide Location switch. Approximate permission is supported. A startup attempt runs once per app launch, so relaunch after changing permissions or the Location switch. New builds log creation, state changes and the final sample or failure with the `[Location]` prefix.

Reproduce failures in the installed app and inspect Logcat before rebuilding. Include the `sherpa-onnx` tag alongside `Unity`, since native model-loading errors do not necessarily appear in Unity's own log. Logs saved under `Logs/` remain outside version control.

For faster code and asset iterations, enable **Development Build** in the Android profile and select **Patch And Run** under **Build To Device**. Unity transfers changed files; the first patch still prepares its environment. Regular Android builds also use incremental compilation, so keep the existing build cache between iterations. See [Unity Application Patching](https://docs.unity.com/en-us/engine/6000.6/manual/platform-specific/android/developing/testing-and-debugging/app-patching).

Patch installations depend on files outside the APK. Clearing the app's data or cache can remove those files and prevent startup, including `Failed to load 'libmain.so'`. To recover a complete installation, use a regular **Build And Run**, rather than Patch And Run. Change permissions through Android's app permission settings when testing an existing patch installation.

Sherpa token tables use LF line endings, enforced by `.gitattributes`, to keep them compatible with the native Android parser.

## Sherpa Manifests

The **Android manifest** declares operating-system permissions and the application entry point. Sherpa's ASR build validator explicitly checks `Assets/Plugins/Android/AndroidManifest.xml` for `android.permission.RECORD_AUDIO`; that declaration is included.

Sherpa regenerates `streaming-assets-manifest.json` before each build. **Tools > SherpaOnnx > Rebuild StreamingAssets Manifest** can also refresh that model-file index manually. It does not configure Android permissions or install native libraries.
