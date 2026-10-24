# Aptoide listing draft — Fieldnote

This is draft copy for a **non-clinical software demo**. Review it and add the owner’s support and privacy-policy links before submitting it in Aptoide Connect.

## App details

- **Name:** Fieldnote — Clinic Workflow Demo
- **Package name:** `org.fieldnote.clinic`
- **Version:** `1.0` (version code `1`)
- **Suggested category:** Medical, if offered; otherwise Health & Fitness
- **App type:** Free demonstration app
- **Age rating:** Answer Aptoide’s questionnaire accurately; do not guess or represent the app as medically validated
- **Distribution:** Public demo, clearly labeled not for clinical use

## Short description

Non-clinical Android demo for organizing fictional imaging cases and clinician review workflows. Not for patient care.

## Full description

Fieldnote is a demonstration Android app for exploring a local clinic imaging-case workflow.

**This is not a diagnostic tool or a medical device. Do not use it for real patient data, patient care, or clinical decisions.**

The demo lets you:

- Browse fictional imaging-case examples.
- Add fictional, non-identifying case references and staff-entered workflow priorities.
- Search cases and record a clinician-review workflow status.
- Preview a selected image temporarily on the device.
- Optionally configure an Ollama model for preset software-help topics.

The optional assistant explains how to use the app only. It does not inspect cases or images, and it does not provide medical, diagnostic, triage, or treatment advice. Only fixed, predefined app-help prompts are sent to the local/private model server configured by the device owner; case records, images, and free-text messages are not included.

Case details are stored in this app’s private local database. Local database files are **not encrypted**. Selected images are previewed in memory and are not saved by the app. This prototype does not provide authentication, cloud backup, remote synchronization, validated clinical AI, or production patient-data safeguards.

All included case examples are fictional. A qualified medical professional must make every clinical decision using validated clinical systems.

## Privacy disclosures

- **Personal or sensitive data:** The app can store case references and optional context in a local SQLite database. Users must not enter real patient or other sensitive information.
- **AI:** Optional. Disabled until the device owner configures an Ollama model. Preset software-help prompts go only to the configured local/private model endpoint. Case information, images, and user-entered free text are not sent.
- **Images:** Selected images are previewed in memory only. The app does not upload or persist image files.
- **Accounts, advertising, analytics:** None are implemented.
- **Network permission:** Used only for the optional, device-owner-configured AI endpoint. The app has no app-owned remote service or cloud synchronization.
- **Security:** Local SQLite data are unencrypted. This demo is not appropriate for real patient data.

Review the final statements against the exact APK and Aptoide’s current developer declarations before publishing.

## Assets and owner details still required

- Public support email and support website.
- Public privacy-policy URL matching the disclosures above.
- Store screenshots captured from the final, installable release on a real Android device or emulator.
- Promotional graphic, if requested by the store console.
- App icon: `Fieldnote.Mobile\Resources\AppIcon\appicon.svg`.
- Aptoide Connect developer account and completed developer/publisher verification.
- Permanent owner-controlled Android release keystore. The APK currently in `bin\Release` is signed with an Android **debug** certificate and must not be uploaded.
- Owner-approved final description, category, rating, and public release.

## Release checklist

1. Register and sign in to [Aptoide Connect](https://connect.aptoide.com/).
2. Decide on a permanent package-signing key. Back up its keystore and passwords securely; do not commit them or share them in chat.
3. Build a new release APK signed with that key. Keep the same package name and signing key for every future update.
4. Install and smoke-test that exact release APK on an Android device. Check case creation, local persistence, review status, image preview, offline behavior, and the assistant's disabled/configured states.
5. Capture genuine store screenshots from the tested build. Do not use mockups that imply image diagnosis.
6. Add the support contact and public privacy-policy URL. Submit accurate privacy, age-rating, and app-content declarations.
7. Upload the owner-signed release APK to the authenticated Aptoide console, review the rendered listing, and publish only after the app owner approves the final submission.
8. Verify the public listing and install/update flow after Aptoide approves publication.

## Release notes

Initial demonstration release. Includes fictional local case examples, case intake and workflow review, and an optional software-help assistant. No image analysis or clinical functionality is provided.
