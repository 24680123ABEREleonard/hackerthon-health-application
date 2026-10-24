# NEUROLINK-FIELDNOTE

NEUROLINK-FIELDNOTE is a platform for skills similar to systems used by doctors in regional hospitals in Cameroon. It brings together a local workflow prototype, case management, and optional AI-assisted software guidance without acting as a diagnostic medical tool.

## Android app

NEUROLINK-FIELDNOTE is a native Android workflow prototype for organizing imaging cases and clinician review. It is not a diagnostic tool or a medical device.

### Build the Android app

Requirements: .NET 10 SDK, .NET MAUI Android workload, and Android SDK.

```powershell
dotnet restore .\Fieldnote.Mobile\Fieldnote.Mobile.csproj
dotnet build .\Fieldnote.Mobile\Fieldnote.Mobile.csproj -f net10.0-android
```

The debug APK is produced under `Fieldnote.Mobile\bin\Debug\net10.0-android\`. Transfer the APK to an Android device to install it. For a release APK, use `dotnet publish` and configure Android signing before distributing outside development.

### Included

- Native Android interface with Home, Cases, Workflow Assistant, and Settings tabs. The dashboard clearly distinguishes optional software-help AI from unconnected clinical/diagnostic AI.
- Local SQLite database in the app's private storage, including a case table, review status, and audit events.
- Fictional example cases, search/filter, new case intake, and clinician-review status workflow.
- Optional local Ollama integration for fixed app-help topics only. Case records, images, and free-text prompts are never sent to the model.
- Optional image preview held in memory only; the image is never saved in SQLite or transmitted.

### Optional local AI setup

Install Ollama and download a local text model on a trusted machine. In the app's Settings, enter the local Ollama address and model name. The Android emulator can usually reach the host at `http://10.0.2.2:11434`; a physical phone should use a trusted private HTTPS endpoint. Public AI endpoints are rejected. AI is disabled when no model is configured.

The assistant sends only one of four predefined software-help prompts to the configured Ollama endpoint. It has no case context and must not be used for clinical or diagnostic questions.

### Safety and privacy limits

- Do not enter real patient information. This prototype has no authentication or production-grade privacy safeguards.
- SQLite files are local but **not encrypted**.
- Image files are not persisted, but the operating system's file picker may access the selected image for local display.
- The AI helper can be wrong; a qualified clinician makes every clinical decision.
- Example cases and all patient references are fictional.

The original browser demonstration remains available at `index.html`; it uses browser local storage only and does not connect to a clinical AI backend. The native Android implementation in `Fieldnote.Mobile\` uses SQLite and can optionally enable a local AI helper for software-help prompts only.
>>>>>>> 6748c79b69649f30e26d57e48742afa436d0b2ff
