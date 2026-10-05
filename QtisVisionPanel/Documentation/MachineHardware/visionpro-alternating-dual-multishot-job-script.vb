Imports System
Imports System.Reflection
Imports Cognex.VisionPro
Imports Cognex.VisionPro.QuickBuild
Imports Cognex.VisionPro.ImageProcessing

' VisionPro QuickBuild Job Script for standard and alternating dual-illumination
' MultiShot acquisition on a single physical Left or Right camera.
'
' Required ToolBlock: ImageStitching
'
' Required ImageStitching inputs:
'   expectedFrames                Double   Total physical triggers. In Dual mode it must be even.
'   stepMm                       Double   Physical distance between consecutive triggers.
'   mmPerPixel                   Double   Camera calibration.
'
' Optional ImageStitching inputs:
'   dualIlluminationEnabled      Boolean  False = legacy single stream; True = Front/Back alternating.
'   frontFirst                   Boolean  Default True.
'   reverseDirection             Boolean  Default False.
'   resetSession                Boolean  Rising edge resets the current session.
'   frameTimeoutMs               Double   Default 1500 ms.
'   maxSessionMs                 Double   Default 5000 ms.
'   sealingInspectionToolName    String   Default SealingInspection.
'   rollInspectionToolName       String   Default RollInspection.
'   inspectionImageInputName     String   Default InputImage.
'   requireInspectionTargets     Boolean  Default True in Dual mode.
'
' Required ImageStitching outputs used by the HMI:
'   isReady                      Boolean
'   frameIndex                   Double
'   status                       String
'   errorMessage                 String
'
' Recommended diagnostic/image outputs:
'   sealingImage                 Cognex.VisionPro.ICogImage
'   rollImage                    Cognex.VisionPro.ICogImage
'   frontFrameIndex              Double
'   backFrameIndex               Double
'   frontReady                   Boolean
'   backReady                    Boolean
'   phase                        String
'   stepPx                       Double
'   phaseStepPx                  Double
'
' Dual mode contract compatible with the current HMI:
'   expectedFrames = total physical triggers = 2 * frames per illumination
'   stepMm        = distance between consecutive Front/Back triggers
'   phaseStepMm   = stepMm * 2, calculated internally
'
' The HMI configures the DALSA Cycling Presets through OwnedGigEAccess and arms
' cyclingPresetResetCmd before a valid session. The camera selects Line3/Line4
' before exposure on each StartOfFrame. PostAcquisitionRef must not drive lights:
' it is called after exposure and only separates/stitches the acquired frames.

Public Class UserScript
  Inherits CogJobBaseScript

  Private Const ControlToolBlockName As String = "ImageStitching"
  Private Const DefaultSealingInspectionToolName As String = "SealingInspection"
  Private Const DefaultRollInspectionToolName As String = "RollInspection"
  Private Const DefaultInspectionImageInputName As String = "InputImage"

  Private Const DefaultFrameTimeoutMs As Integer = 1500
  Private Const DefaultMaxSessionMs As Integer = 5000
  Private Const MaxExpectedFramesLimit As Integer = 100
  Private Const MaxCanvasWidthPixels As Integer = 100000
  Private Const MaxCanvasPixels As Long = 250000000L

  Private currentJob As CogJob
  Private controlToolBlock As Object

  Private singleStitcher As CogCopyRegionTool
  Private sealingStitcher As CogCopyRegionTool
  Private rollStitcher As CogCopyRegionTool

  Private totalCounter As Integer
  Private sealingCounter As Integer
  Private rollCounter As Integer

  Private sessionExpectedTotalFrames As Integer
  Private sessionExpectedFramesPerPhase As Integer
  Private sessionPhysicalStepPx As Integer
  Private sessionPhaseStepPx As Integer
  Private sessionReverseDirection As Boolean
  Private sessionDualMode As Boolean
  Private sessionFrontFirst As Boolean
  Private sessionWidth As Integer
  Private sessionHeight As Integer
  Private sessionFrameTimeoutMs As Integer
  Private sessionMaxSessionMs As Integer
  Private sessionStartedUtc As DateTime
  Private lastFrameUtc As DateTime
  Private sessionCompleted As Boolean
  Private lastResetRequest As Boolean

  Overrides Sub Initialize(ByVal jobParam As CogJob)
    MyBase.Initialize(jobParam)

    currentJob = jobParam
    controlToolBlock = Nothing
    singleStitcher = CreateStitcher()
    sealingStitcher = CreateStitcher()
    rollStitcher = CreateStitcher()

    ResetSessionState()
    lastResetRequest = False
    ClearPublishedImages()
    PublishIdleStatus("Initialized")
  End Sub

  Overrides Function PostAcquisitionRef(ByRef image As ICogImage) As Boolean
    Try
      Dim nowUtc As DateTime = DateTime.UtcNow
      HandleResetRequest()

      If sessionCompleted Then
        ResetSessionState()
        ClearPublishedImages()
        PublishIdleStatus("New session")
      End If

      If totalCounter > 0 AndAlso IsSessionExpired(nowUtc) Then
        Dim previousCounter As Integer = totalCounter
        ResetSessionState()
        ClearPublishedImages()
        PublishIdleStatus("Stale session reset; previous frame count=" & previousCounter.ToString())
      End If

      Dim greyImage As CogImage8Grey = TryCast(image, CogImage8Grey)
      If greyImage Is Nothing Then
        Fail("Image stitching supports only CogImage8Grey.")
      End If

      Dim requestedFrames As Integer = GetStitchInputInt("expectedFrames", -1)
      Dim dualMode As Boolean = GetStitchInputBool("dualIlluminationEnabled", False)

      If requestedFrames <= 0 Then
        Fail("expectedFrames not found or invalid in ImageStitching.")
      End If

      If requestedFrames > MaxExpectedFramesLimit Then
        Fail("expectedFrames is too high. Maximum supported value is " & MaxExpectedFramesLimit.ToString() & ".")
      End If

      ' Important legacy fix: do not continue into an uninitialized stitcher.
      If requestedFrames = 1 Then
        If dualMode Then
          Fail("Dual illumination requires an even expectedFrames value of at least 2.")
        End If

        PublishSingleFrame(greyImage, image)
        Return True
      End If

      If totalCounter = 0 Then
        StartNewSession(greyImage, nowUtc, requestedFrames, dualMode)
      Else
        ValidateSessionStillCompatible(greyImage, requestedFrames, dualMode)
      End If

      If sessionDualMode Then
        ProcessDualFrame(greyImage)
      Else
        ProcessStandardFrame(greyImage)
      End If

      lastFrameUtc = nowUtc

      If totalCounter < sessionExpectedTotalFrames Then
        SetStitchOutput("isReady", False)
        Return False
      End If

      If sessionDualMode Then
        CompleteDualSession(image)
      Else
        CompleteStandardSession(image)
      End If

      sessionCompleted = True
      Return True

    Catch ex As Exception
      SetStitchOutput("isReady", False)
      SetStitchOutput("frontReady", False)
      SetStitchOutput("backReady", False)
      SetStitchOutput("status", "Error")
      SetStitchOutput("errorMessage", ex.Message)
      ResetSessionState()
      Throw
    End Try
  End Function

  Private Sub StartNewSession(
      ByVal greyImage As CogImage8Grey,
      ByVal nowUtc As DateTime,
      ByVal expectedTotalFrames As Integer,
      ByVal dualMode As Boolean)

    Dim stepMm As Double = GetStitchInputDouble("stepMm", -1.0)
    Dim mmPerPixel As Double = GetStitchInputDouble("mmPerPixel", -1.0)

    If stepMm <= 0 Then
      Fail("stepMm not found or invalid in ImageStitching.")
    End If

    If mmPerPixel <= 0 Then
      Fail("mmPerPixel not found or invalid in ImageStitching.")
    End If

    If dualMode AndAlso (expectedTotalFrames < 2 OrElse (expectedTotalFrames Mod 2) <> 0) Then
      Fail("Dual illumination requires an even expectedFrames value. It represents all Front and Backlight triggers.")
    End If

    sessionDualMode = dualMode
    sessionExpectedTotalFrames = expectedTotalFrames
    sessionExpectedFramesPerPhase = If(dualMode, expectedTotalFrames \ 2, expectedTotalFrames)
    sessionPhysicalStepPx = CInt(Math.Round(stepMm / mmPerPixel))
    sessionPhaseStepPx = If(dualMode, sessionPhysicalStepPx * 2, sessionPhysicalStepPx)
    sessionReverseDirection = GetStitchInputBool("reverseDirection", False)
    sessionFrontFirst = GetStitchInputBool("frontFirst", True)
    sessionWidth = greyImage.Width
    sessionHeight = greyImage.Height
    sessionFrameTimeoutMs = NormalizeFrameTimeout(GetStitchInputInt("frameTimeoutMs", DefaultFrameTimeoutMs))
    sessionMaxSessionMs = NormalizeSessionTimeout(
      GetStitchInputInt("maxSessionMs", DefaultMaxSessionMs),
      sessionFrameTimeoutMs)
    sessionStartedUtc = nowUtc
    lastFrameUtc = nowUtc

    If sessionPhysicalStepPx <= 0 Then
      Fail("Calculated physical stepPx is not valid.")
    End If

    If sessionPhaseStepPx <= 0 Then
      Fail("Calculated phaseStepPx is not valid.")
    End If

    If sessionPhaseStepPx >= greyImage.Width Then
      Fail("phaseStepPx is greater than or equal to image width. Check stepMm/mmPerPixel.")
    End If

    ClearPublishedImages()

    If sessionDualMode Then
      ConfigureStitcher(sealingStitcher, greyImage, sessionExpectedFramesPerPhase, sessionPhaseStepPx)
      ConfigureStitcher(rollStitcher, greyImage, sessionExpectedFramesPerPhase, sessionPhaseStepPx)
    Else
      ConfigureStitcher(singleStitcher, greyImage, sessionExpectedTotalFrames, sessionPhysicalStepPx)
    End If

    SetStitchOutput("isReady", False)
    SetStitchOutput("frontReady", False)
    SetStitchOutput("backReady", False)
    SetStitchOutput("frameIndex", 0)
    SetStitchOutput("frontFrameIndex", 0)
    SetStitchOutput("backFrameIndex", 0)
    SetStitchOutput("stepPx", sessionPhysicalStepPx)
    SetStitchOutput("phaseStepPx", sessionPhaseStepPx)
    SetStitchOutput("phase", If(sessionDualMode, "Waiting first phase", "Standard"))
    SetStitchOutput("errorMessage", "")

    If sessionDualMode Then
      SetStitchOutput(
        "status",
        "Dual init: total=" & sessionExpectedTotalFrames.ToString() &
        ", perPhase=" & sessionExpectedFramesPerPhase.ToString() &
        ", physicalStepPx=" & sessionPhysicalStepPx.ToString() &
        ", phaseStepPx=" & sessionPhaseStepPx.ToString())
    Else
      SetStitchOutput(
        "status",
        "Standard init: frames=" & sessionExpectedTotalFrames.ToString() &
        ", stepPx=" & sessionPhysicalStepPx.ToString())
    End If
  End Sub

  Private Sub ProcessStandardFrame(ByVal greyImage As CogImage8Grey)
    Dim frameIndex As Integer = totalCounter + 1
    CopyFrame(
      singleStitcher,
      greyImage,
      frameIndex,
      sessionExpectedTotalFrames,
      sessionPhysicalStepPx)

    totalCounter = frameIndex

    SetStitchOutput("frameIndex", totalCounter)
    SetStitchOutput("frontFrameIndex", totalCounter)
    SetStitchOutput("backFrameIndex", 0)
    SetStitchOutput("phase", "Standard")
    SetStitchOutput("isReady", False)
    SetStitchOutput(
      "status",
      "Copied standard frame " & totalCounter.ToString() & "/" & sessionExpectedTotalFrames.ToString())
    SetStitchOutput("errorMessage", "")
  End Sub

  Private Sub ProcessDualFrame(ByVal greyImage As CogImage8Grey)
    Dim zeroBasedPhysicalIndex As Integer = totalCounter
    Dim isFrontFrame As Boolean

    If sessionFrontFirst Then
      isFrontFrame = (zeroBasedPhysicalIndex Mod 2) = 0
    Else
      isFrontFrame = (zeroBasedPhysicalIndex Mod 2) <> 0
    End If

    If isFrontFrame Then
      Dim nextFrontIndex As Integer = sealingCounter + 1
      CopyFrame(
        sealingStitcher,
        greyImage,
        nextFrontIndex,
        sessionExpectedFramesPerPhase,
        sessionPhaseStepPx)
      sealingCounter = nextFrontIndex
      SetStitchOutput("phase", "Front")
    Else
      Dim nextBackIndex As Integer = rollCounter + 1
      CopyFrame(
        rollStitcher,
        greyImage,
        nextBackIndex,
        sessionExpectedFramesPerPhase,
        sessionPhaseStepPx)
      rollCounter = nextBackIndex
      SetStitchOutput("phase", "Backlight")
    End If

    totalCounter += 1

    SetStitchOutput("frameIndex", totalCounter)
    SetStitchOutput("frontFrameIndex", sealingCounter)
    SetStitchOutput("backFrameIndex", rollCounter)
    SetStitchOutput("frontReady", sealingCounter = sessionExpectedFramesPerPhase)
    SetStitchOutput("backReady", rollCounter = sessionExpectedFramesPerPhase)
    SetStitchOutput("isReady", False)
    SetStitchOutput(
      "status",
      "Dual frame " & totalCounter.ToString() & "/" & sessionExpectedTotalFrames.ToString() &
      "; Front=" & sealingCounter.ToString() & "/" & sessionExpectedFramesPerPhase.ToString() &
      "; Back=" & rollCounter.ToString() & "/" & sessionExpectedFramesPerPhase.ToString())
    SetStitchOutput("errorMessage", "")
  End Sub

  Private Sub CompleteStandardSession(ByRef image As ICogImage)
    Dim stitchedImage As ICogImage = TryCast(singleStitcher.OutputImage, ICogImage)
    If stitchedImage Is Nothing Then
      Fail("Standard stitching completed without an output image.")
    End If

    image = stitchedImage
    SetStitchOutput("sealingImage", stitchedImage)
    SetStitchOutput("rollImage", Nothing)

    Dim ignoredReason As String = ""
    Dim sealingToolName As String = GetStitchInputString(
      "sealingInspectionToolName",
      DefaultSealingInspectionToolName)
    Dim imageInputName As String = GetStitchInputString(
      "inspectionImageInputName",
      DefaultInspectionImageInputName)

    ' Optional in legacy mode because the existing inspection chain can consume image directly.
    TrySetToolInputValue(sealingToolName, imageInputName, stitchedImage, ignoredReason)

    SetStitchOutput("frontReady", True)
    SetStitchOutput("backReady", False)
    SetStitchOutput("isReady", True)
    SetStitchOutput("frameIndex", totalCounter)
    SetStitchOutput("status", "Ready - standard stitching")
    SetStitchOutput("errorMessage", "")
  End Sub

  Private Sub CompleteDualSession(ByRef image As ICogImage)
    If sealingCounter <> sessionExpectedFramesPerPhase OrElse
       rollCounter <> sessionExpectedFramesPerPhase Then
      Fail(
        "Dual stitching ended with an invalid phase count. Front=" & sealingCounter.ToString() &
        ", Back=" & rollCounter.ToString() &
        ", expected=" & sessionExpectedFramesPerPhase.ToString() & ".")
    End If

    Dim sealingImage As ICogImage = TryCast(sealingStitcher.OutputImage, ICogImage)
    Dim rollImage As ICogImage = TryCast(rollStitcher.OutputImage, ICogImage)

    If sealingImage Is Nothing Then
      Fail("Dual stitching completed without sealingImage.")
    End If

    If rollImage Is Nothing Then
      Fail("Dual stitching completed without rollImage.")
    End If

    Dim sealingToolName As String = GetStitchInputString(
      "sealingInspectionToolName",
      DefaultSealingInspectionToolName)
    Dim rollToolName As String = GetStitchInputString(
      "rollInspectionToolName",
      DefaultRollInspectionToolName)
    Dim imageInputName As String = GetStitchInputString(
      "inspectionImageInputName",
      DefaultInspectionImageInputName)
    Dim requireTargets As Boolean = GetStitchInputBool("requireInspectionTargets", True)

    Dim sealingReason As String = ""
    Dim rollReason As String = ""
    Dim sealingInjected As Boolean = TrySetToolInputValue(
      sealingToolName,
      imageInputName,
      sealingImage,
      sealingReason)
    Dim rollInjected As Boolean = TrySetToolInputValue(
      rollToolName,
      imageInputName,
      rollImage,
      rollReason)

    If requireTargets AndAlso Not sealingInjected Then
      Fail("Cannot publish sealingImage to " & sealingToolName & "." & imageInputName & ": " & sealingReason)
    End If

    If requireTargets AndAlso Not rollInjected Then
      Fail("Cannot publish rollImage to " & rollToolName & "." & imageInputName & ": " & rollReason)
    End If

    ' Keep the Front/sealing composite as the normal acquired image for legacy tools and display.
    image = sealingImage

    SetStitchOutput("sealingImage", sealingImage)
    SetStitchOutput("rollImage", rollImage)
    SetStitchOutput("frontReady", True)
    SetStitchOutput("backReady", True)
    SetStitchOutput("isReady", True)
    SetStitchOutput("frameIndex", totalCounter)
    SetStitchOutput("frontFrameIndex", sealingCounter)
    SetStitchOutput("backFrameIndex", rollCounter)
    SetStitchOutput("phase", "Complete")
    SetStitchOutput("status", "Ready - sealingImage and rollImage completed")
    SetStitchOutput("errorMessage", "")
  End Sub

  Private Sub PublishSingleFrame(ByVal greyImage As CogImage8Grey, ByRef image As ICogImage)
    ResetSessionState()
    totalCounter = 1
    sealingCounter = 1
    sessionExpectedTotalFrames = 1
    sessionExpectedFramesPerPhase = 1

    image = greyImage
    SetStitchOutput("sealingImage", greyImage)
    SetStitchOutput("rollImage", Nothing)

    Dim ignoredReason As String = ""
    Dim sealingToolName As String = GetStitchInputString(
      "sealingInspectionToolName",
      DefaultSealingInspectionToolName)
    Dim imageInputName As String = GetStitchInputString(
      "inspectionImageInputName",
      DefaultInspectionImageInputName)
    TrySetToolInputValue(sealingToolName, imageInputName, greyImage, ignoredReason)

    SetStitchOutput("frameIndex", 1)
    SetStitchOutput("frontFrameIndex", 1)
    SetStitchOutput("backFrameIndex", 0)
    SetStitchOutput("frontReady", True)
    SetStitchOutput("backReady", False)
    SetStitchOutput("phase", "SingleShot")
    SetStitchOutput("isReady", True)
    SetStitchOutput("status", "SingleShot")
    SetStitchOutput("errorMessage", "")
    sessionCompleted = True
  End Sub

  Private Sub CopyFrame(
      ByVal stitcher As CogCopyRegionTool,
      ByVal greyImage As CogImage8Grey,
      ByVal phaseFrameIndex As Integer,
      ByVal expectedPhaseFrames As Integer,
      ByVal stepPx As Integer)

    If stitcher Is Nothing OrElse stitcher.DestinationImage Is Nothing Then
      Fail("Stitching destination is not initialized.")
    End If

    If phaseFrameIndex <= 0 OrElse phaseFrameIndex > expectedPhaseFrames Then
      Fail(
        "Frame index " & phaseFrameIndex.ToString() &
        " is outside expected phase range 1.." & expectedPhaseFrames.ToString() & ".")
    End If

    Dim destX As Double
    If sessionReverseDirection Then
      destX = (expectedPhaseFrames - phaseFrameIndex) * stepPx
    Else
      destX = (phaseFrameIndex - 1) * stepPx
    End If

    stitcher.RunParams.DestinationImageAlignmentX = destX
    stitcher.RunParams.DestinationImageAlignmentY = 0
    stitcher.InputImage = greyImage
    stitcher.Run()
  End Sub

  Private Sub ConfigureStitcher(
      ByVal stitcher As CogCopyRegionTool,
      ByVal sourceImage As CogImage8Grey,
      ByVal expectedFrames As Integer,
      ByVal stepPx As Integer)

    If stitcher Is Nothing Then
      Fail("CogCopyRegionTool is not initialized.")
    End If

    Dim canvasWidth As Long = CLng(sourceImage.Width) +
      CLng(stepPx) * CLng(expectedFrames - 1)
    Dim canvasPixels As Long = canvasWidth * CLng(sourceImage.Height)

    If canvasWidth <= 0 OrElse canvasWidth > MaxCanvasWidthPixels Then
      Fail("Calculated stitched canvas width is outside the safety limit: " & canvasWidth.ToString() & " px.")
    End If

    If canvasPixels <= 0 OrElse canvasPixels > MaxCanvasPixels Then
      Fail("Calculated stitched canvas size is outside the safety limit: " & canvasPixels.ToString() & " pixels.")
    End If

    Dim stitchedImage As New CogImage8Grey()
    stitchedImage.Allocate(CInt(canvasWidth), sourceImage.Height)

    stitcher.DestinationImage = stitchedImage
    stitcher.Region = Nothing
    stitcher.RunParams.ImageAlignmentEnabled = True
  End Sub

  Private Function CreateStitcher() As CogCopyRegionTool
    Dim stitcher As New CogCopyRegionTool()
    stitcher.Region = Nothing
    stitcher.RunParams.ImageAlignmentEnabled = True
    Return stitcher
  End Function

  Private Sub ValidateSessionStillCompatible(
      ByVal greyImage As CogImage8Grey,
      ByVal configuredExpectedFrames As Integer,
      ByVal configuredDualMode As Boolean)

    If greyImage.Width <> sessionWidth OrElse greyImage.Height <> sessionHeight Then
      Fail(
        "Frame size changed inside stitching session. Expected " &
        sessionWidth.ToString() & "x" & sessionHeight.ToString() &
        ", got " & greyImage.Width.ToString() & "x" & greyImage.Height.ToString() & ".")
    End If

    If configuredExpectedFrames <> sessionExpectedTotalFrames Then
      Fail("expectedFrames changed during an active stitching session.")
    End If

    If configuredDualMode <> sessionDualMode Then
      Fail("dualIlluminationEnabled changed during an active stitching session.")
    End If

    If totalCounter >= sessionExpectedTotalFrames Then
      Fail("Received an extra frame after expectedFrames. Check trigger count and session reset.")
    End If
  End Sub

  Private Sub HandleResetRequest()
    Dim resetRequest As Boolean = GetStitchInputBool("resetSession", False)

    If resetRequest AndAlso Not lastResetRequest Then
      ResetSessionState()
      ClearPublishedImages()
      PublishIdleStatus("Reset requested")
    End If

    lastResetRequest = resetRequest
  End Sub

  Private Function IsSessionExpired(ByVal nowUtc As DateTime) As Boolean
    If lastFrameUtc <> DateTime.MinValue AndAlso
       (nowUtc - lastFrameUtc).TotalMilliseconds > sessionFrameTimeoutMs Then
      Return True
    End If

    If sessionStartedUtc <> DateTime.MinValue AndAlso
       (nowUtc - sessionStartedUtc).TotalMilliseconds > sessionMaxSessionMs Then
      Return True
    End If

    Return False
  End Function

  Private Function NormalizeFrameTimeout(ByVal value As Integer) As Integer
    If value < 100 Then Return DefaultFrameTimeoutMs
    Return value
  End Function

  Private Function NormalizeSessionTimeout(ByVal value As Integer, ByVal frameTimeout As Integer) As Integer
    If value < frameTimeout Then
      Return Math.Max(DefaultMaxSessionMs, frameTimeout * 2)
    End If

    Return value
  End Function

  Private Sub ResetSessionState()
    totalCounter = 0
    sealingCounter = 0
    rollCounter = 0
    sessionExpectedTotalFrames = 0
    sessionExpectedFramesPerPhase = 0
    sessionPhysicalStepPx = 0
    sessionPhaseStepPx = 0
    sessionReverseDirection = False
    sessionDualMode = False
    sessionFrontFirst = True
    sessionWidth = 0
    sessionHeight = 0
    sessionFrameTimeoutMs = DefaultFrameTimeoutMs
    sessionMaxSessionMs = DefaultMaxSessionMs
    sessionStartedUtc = DateTime.MinValue
    lastFrameUtc = DateTime.MinValue
    sessionCompleted = False
  End Sub

  Private Sub ClearPublishedImages()
    SetStitchOutput("sealingImage", Nothing)
    SetStitchOutput("rollImage", Nothing)
  End Sub

  Private Sub PublishIdleStatus(ByVal statusText As String)
    SetStitchOutput("isReady", False)
    SetStitchOutput("frameIndex", 0)
    SetStitchOutput("frontFrameIndex", 0)
    SetStitchOutput("backFrameIndex", 0)
    SetStitchOutput("frontReady", False)
    SetStitchOutput("backReady", False)
    SetStitchOutput("phase", "Idle")
    SetStitchOutput("status", statusText)
    SetStitchOutput("errorMessage", "")
  End Sub

  Private Sub Fail(ByVal message As String)
    SetStitchOutput("isReady", False)
    SetStitchOutput("frontReady", False)
    SetStitchOutput("backReady", False)
    SetStitchOutput("status", "Error")
    SetStitchOutput("errorMessage", message)
    Throw New Exception(message)
  End Sub

  Private Function GetStitchInputInt(ByVal inputName As String, ByVal defaultValue As Integer) As Integer
    Dim value As Object = GetImageStitchingInputValue(inputName)
    If value Is Nothing Then Return defaultValue

    Try
      Return Convert.ToInt32(value)
    Catch
      Return defaultValue
    End Try
  End Function

  Private Function GetStitchInputDouble(ByVal inputName As String, ByVal defaultValue As Double) As Double
    Dim value As Object = GetImageStitchingInputValue(inputName)
    If value Is Nothing Then Return defaultValue

    Try
      Return Convert.ToDouble(value)
    Catch
      Return defaultValue
    End Try
  End Function

  Private Function GetStitchInputBool(ByVal inputName As String, ByVal defaultValue As Boolean) As Boolean
    Dim value As Object = GetImageStitchingInputValue(inputName)
    If value Is Nothing Then Return defaultValue

    Try
      Return Convert.ToBoolean(value)
    Catch
      Return defaultValue
    End Try
  End Function

  Private Function GetStitchInputString(ByVal inputName As String, ByVal defaultValue As String) As String
    Dim value As Object = GetImageStitchingInputValue(inputName)
    If value Is Nothing Then Return defaultValue

    Dim text As String = value.ToString().Trim()
    If text.Length = 0 Then Return defaultValue
    Return text
  End Function

  Private Function GetImageStitchingInputValue(ByVal inputName As String) As Object
    Dim imageStitchingTool As Object = GetImageStitchingToolObject()
    If imageStitchingTool Is Nothing Then Return Nothing

    Dim inputs As Object = GetPropertyValue(imageStitchingTool, "Inputs")
    If inputs Is Nothing Then Return Nothing

    Dim terminal As Object = GetIndexedObject(inputs, inputName)
    If terminal Is Nothing Then Return Nothing

    Return GetPropertyValue(terminal, "Value")
  End Function

  Private Sub SetStitchOutput(ByVal outputName As String, ByVal value As Object)
    Dim imageStitchingTool As Object = GetImageStitchingToolObject()
    If imageStitchingTool Is Nothing Then Return

    Dim outputs As Object = GetPropertyValue(imageStitchingTool, "Outputs")
    If outputs Is Nothing Then Return

    Dim terminal As Object = GetIndexedObject(outputs, outputName)
    If terminal Is Nothing Then Return

    Try
      Dim prop As PropertyInfo = terminal.GetType().GetProperty("Value")
      If prop IsNot Nothing Then
        prop.SetValue(terminal, value, Nothing)
      End If
    Catch
      ' Optional diagnostic outputs may be absent. Required outputs are listed above.
    End Try
  End Sub

  Private Function TrySetToolInputValue(
      ByVal toolName As String,
      ByVal inputName As String,
      ByVal value As Object,
      ByRef reason As String) As Boolean

    reason = ""

    If String.IsNullOrWhiteSpace(toolName) Then
      reason = "Tool name is empty"
      Return False
    End If

    If String.IsNullOrWhiteSpace(inputName) Then
      reason = "Input name is empty"
      Return False
    End If

    Dim root As Object = GetJobVisionTool()
    Dim targetTool As Object = FindToolRecursive(root, toolName.Trim(), 0)
    If targetTool Is Nothing Then
      reason = "ToolBlock not found"
      Return False
    End If

    Dim inputs As Object = GetPropertyValue(targetTool, "Inputs")
    If inputs Is Nothing Then
      reason = "Inputs collection not available"
      Return False
    End If

    Dim terminal As Object = GetIndexedObject(inputs, inputName.Trim())
    If terminal Is Nothing Then
      reason = "Input terminal not found"
      Return False
    End If

    Try
      Dim prop As PropertyInfo = terminal.GetType().GetProperty("Value")
      If prop Is Nothing Then
        reason = "Input Value property not available"
        Return False
      End If

      prop.SetValue(terminal, value, Nothing)
      Return True
    Catch ex As Exception
      reason = ex.Message
      Return False
    End Try
  End Function

  Private Function GetImageStitchingToolObject() As Object
    If controlToolBlock IsNot Nothing Then Return controlToolBlock

    controlToolBlock = FindToolRecursive(GetJobVisionTool(), ControlToolBlockName, 0)
    Return controlToolBlock
  End Function

  Private Function GetJobVisionTool() As Object
    If currentJob Is Nothing Then Return Nothing

    Try
      Return currentJob.VisionTool
    Catch
      Return Nothing
    End Try
  End Function

  Private Function FindToolRecursive(
      ByVal tool As Object,
      ByVal targetName As String,
      ByVal depth As Integer) As Object

    If tool Is Nothing Then Return Nothing
    If depth > 16 Then Return Nothing

    Dim name As String = GetStringProperty(tool, "Name")
    If String.Equals(name, targetName, StringComparison.OrdinalIgnoreCase) Then
      Return tool
    End If

    Dim toolsCollection As Object = GetPropertyValue(tool, "Tools")
    If toolsCollection Is Nothing Then Return Nothing

    Dim direct As Object = GetIndexedObject(toolsCollection, targetName)
    If direct IsNot Nothing Then Return direct

    Dim count As Integer = GetIntProperty(toolsCollection, "Count", -1)
    If count <= 0 Then Return Nothing

    For i As Integer = 0 To count - 1
      Dim child As Object = GetIndexedObject(toolsCollection, i)
      Dim found As Object = FindToolRecursive(child, targetName, depth + 1)
      If found IsNot Nothing Then Return found
    Next

    Return Nothing
  End Function

  Private Function GetPropertyValue(ByVal obj As Object, ByVal propertyName As String) As Object
    If obj Is Nothing Then Return Nothing

    Try
      Dim prop As PropertyInfo = obj.GetType().GetProperty(propertyName)
      If prop Is Nothing Then Return Nothing
      Return prop.GetValue(obj, Nothing)
    Catch
      Return Nothing
    End Try
  End Function

  Private Function GetStringProperty(ByVal obj As Object, ByVal propertyName As String) As String
    Dim value As Object = GetPropertyValue(obj, propertyName)
    If value Is Nothing Then Return String.Empty
    Return value.ToString()
  End Function

  Private Function GetIntProperty(
      ByVal obj As Object,
      ByVal propertyName As String,
      ByVal defaultValue As Integer) As Integer

    Dim value As Object = GetPropertyValue(obj, propertyName)
    If value Is Nothing Then Return defaultValue

    Try
      Return Convert.ToInt32(value)
    Catch
      Return defaultValue
    End Try
  End Function

  Private Function GetIndexedObject(ByVal collection As Object, ByVal index As Object) As Object
    If collection Is Nothing OrElse index Is Nothing Then Return Nothing

    Try
      Dim props As PropertyInfo() = collection.GetType().GetProperties()
      For i As Integer = 0 To props.Length - 1
        Dim prop As PropertyInfo = props(i)
        If prop.Name <> "Item" Then Continue For

        Dim parameters As ParameterInfo() = prop.GetIndexParameters()
        If parameters Is Nothing OrElse parameters.Length <> 1 Then Continue For

        Dim parameterType As Type = parameters(0).ParameterType
        Dim convertedIndex As Object = Nothing

        Try
          convertedIndex = Convert.ChangeType(index, parameterType)
        Catch
          Continue For
        End Try

        Return prop.GetValue(collection, New Object() {convertedIndex})
      Next
    Catch
    End Try

    Return Nothing
  End Function
End Class
