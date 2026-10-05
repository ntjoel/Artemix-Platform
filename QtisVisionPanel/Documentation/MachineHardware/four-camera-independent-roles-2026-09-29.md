# Four independent camera roles (2026-09-29)

## Contract

The VisionPro jobs TOP, LEFT, RIGHT and REAR have distinct runtime identities. SIDE remains the historical alias for LEFT. The VPP job name identifies the role; `CameraConfig.xml` and configured camera serials are fallbacks for generic job names. `Config.xml` gains the optional `RightCameraSerial` key. An absent value leaves role selection to the VPP name or `CameraConfig.xml`.

Each role now has its own result queue, trigger correlation, inspection cells and accepted AI classes. RIGHT and REAR have separate validation results, display records, ONNX profiles, labels (`right`/`rear`) and image tags (`_RI_`/`_R_`). `tblgenerale` gains nullable `RightClassificationLabel`, `RightClassificationScore` and `NC_RightAiClassification` columns through the existing additive startup migration. REAR keeps its existing columns. The per-camera measurement capture also records RIGHT under its own role.

## Machine and recipe configuration

- The REAR physical trigger remains `CAMERA_TRIGGER_REAR` / `OUT_CAMERA_REAR_TRIGGER`.
- The RIGHT trigger is `CAMERA_TRIGGER_RIGHT` / `OUT_CAMERA_RIGHT_TRIGGER`. Provisioning creates an unassigned output and a disabled intervention point. Set the board, a **different** DO channel, position and enablement during commissioning. The runtime no longer resolves RIGHT as an alias for REAR.
- Recipe `CameraTriggers.Right` / `CameraTriggers.Rear`, position offsets, hardware trigger delays, inspection view profiles and RIGHT/REAR sealing thresholds are independent when both physical jobs exist. On a historical VPP with only REAR, the earlier RIGHT recipe values remain a fallback.
- MultiShot gains a separate `Rear` profile and recipe adjustment. The historical profile key `Right` retains its stored `CameraRole` and trigger output. When both jobs are present, provisioning upgrades only an untouched, disabled historical default to the RIGHT output. An enabled legacy profile is preserved. The HMI shows its role and output for commissioning; verify both before enabling it. A profile collision on role or output disables the new REAR profile and logs `MULTISHOT_PROFILE_COLLISION`.

## Commissioning checks

1. Load the four-job VPP and confirm TOP, LEFT, RIGHT and REAR are shown separately in the camera container and inspection configuration.
2. Assign distinct DO channels to RIGHT and REAR. Check `OUT_CAMERA_RIGHT_TRIGGER` and `OUT_CAMERA_REAR_TRIGGER` with the machine stopped before enabling their points.
3. Set each recipe camera mode and inspection matrix independently. Trigger one product and check that both RIGHT and REAR results arrive before the product is finalized.
4. Check that a RIGHT-only fault marks RIGHT, its classification DB fields and `labels.right`, while REAR remains unchanged. Repeat with a REAR-only fault.
5. If MultiShot is used, check `CameraRole`, output channel and resulting pulse on each profile. An old enabled `Right` profile may still target REAR by design.

Build and reflection checks cover role normalization, four-job mapping, inspection cells, ONNX image tags and legacy/new MultiShot role matching. Real VisionPro, board output and production DB behavior still require machine commissioning.
