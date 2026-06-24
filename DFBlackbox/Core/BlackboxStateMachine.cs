using DFBlackbox.Models;

namespace DFBlackbox.Core;

public sealed class BlackboxStateMachine
{
    private readonly AppSettings _settings;
    private DateTime _lastMotionTime = DateTime.MinValue;
    private DateTime? _stableHomeSince;
    private DateTime _cooldownUntil = DateTime.MinValue;
    private bool _manualRecordingActive;

    public BlackboxStateMachine(AppSettings settings)
    {
        _settings = settings;
    }

    public BlackboxState CurrentState { get; private set; } = BlackboxState.Idle;
    public bool AutoRecordingEnabled { get; set; }

    public StateUpdateResult Update(DetectionResult result, DateTime now)
    {
        var anyMotion = result.PersonDetected;
        if (anyMotion)
        {
            _lastMotionTime = now;
            _stableHomeSince = null;
        }
        else if (result.HomeStable)
        {
            _stableHomeSince ??= now;
        }
        else
        {
            _stableHomeSince = null;
        }

        var secondsSinceLastMotion = _lastMotionTime == DateTime.MinValue
            ? double.MaxValue
            : (now - _lastMotionTime).TotalSeconds;
        var stableHomeDuration = _stableHomeSince.HasValue ? now - _stableHomeSince.Value : TimeSpan.Zero;

        var triggerReason = GetTriggerReason(result);
        var recordingStopWaitSeconds = _settings.Detection.RecordingStopWaitSeconds;
        var shouldStartRecording = CurrentState != BlackboxState.Recording
            && now >= _cooldownUntil
            && (_manualRecordingActive || (AutoRecordingEnabled && triggerReason.Length > 0));
        var shouldKeepRecording = result.PersonDetected
            || !result.HomeStable
            || secondsSinceLastMotion < recordingStopWaitSeconds
            || _manualRecordingActive;
        var canStopRecording = !_manualRecordingActive
            && !result.PersonDetected
            && result.HomeStable
            && stableHomeDuration.TotalSeconds >= recordingStopWaitSeconds;
        var recordingHoldReason = GetRecordingHoldReason(
            result,
            recordingStopWaitSeconds,
            secondsSinceLastMotion,
            stableHomeDuration.TotalSeconds,
            _manualRecordingActive);

        var shouldStopRecording = CurrentState == BlackboxState.Recording && canStopRecording;
        if (shouldStartRecording)
        {
            CurrentState = BlackboxState.Recording;
        }
        else if (shouldStopRecording)
        {
            CurrentState = BlackboxState.Cooldown;
            _cooldownUntil = now.AddSeconds(_settings.Detection.CooldownSeconds);
        }
        else if (CurrentState == BlackboxState.Cooldown && now >= _cooldownUntil)
        {
            CurrentState = BlackboxState.Watching;
        }
        else if (CurrentState is BlackboxState.Idle)
        {
            CurrentState = BlackboxState.Watching;
        }
        else if (CurrentState == BlackboxState.Watching && AutoRecordingEnabled && triggerReason.Length > 0)
        {
            CurrentState = BlackboxState.PreEvent;
        }

        return new StateUpdateResult
        {
            ShouldStartRecording = shouldStartRecording,
            ShouldKeepRecording = shouldKeepRecording,
            ShouldStopRecording = shouldStopRecording,
            TriggerReason = _manualRecordingActive ? "Manual" : triggerReason,
            RecordingHoldReason = recordingHoldReason,
            NewState = CurrentState
        };
    }

    public void RequestManualRecordingStart()
    {
        _manualRecordingActive = true;
    }

    public void RequestManualRecordingStop()
    {
        _manualRecordingActive = false;
    }

    public void CompleteRecording(DateTime now)
    {
        _manualRecordingActive = false;
        CurrentState = BlackboxState.Cooldown;
        _cooldownUntil = now.AddSeconds(_settings.Detection.CooldownSeconds);
    }

    private static string GetTriggerReason(DetectionResult result)
    {
        if (result.PersonDetected)
        {
            return "ROI_Diff";
        }

        return "";
    }

    private string GetRecordingHoldReason(
        DetectionResult result,
        int recordingStopWaitSeconds,
        double secondsSinceLastMotion,
        double stableHomeSeconds,
        bool manualRecordingActive)
    {
        if (manualRecordingActive)
        {
            return "KEEP: manual recording";
        }

        if (result.PersonDetected)
        {
            return $"KEEP: ROI_Diff {result.PersonMotionScore:0.000} >= {_settings.Detection.PersonMotionRatioThreshold:0.000}";
        }

        if (!result.HomeStable)
        {
            return $"KEEP: home unstable H={result.HomeStable}";
        }

        if (secondsSinceLastMotion < recordingStopWaitSeconds)
        {
            return $"KEEP: stop wait {recordingStopWaitSeconds - secondsSinceLastMotion:0.0}s";
        }

        return $"STOP READY: stable {stableHomeSeconds:0.0}s";
    }
}
