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
        bool anyMotion = result.PersonDetected;
        // 마지막 움직임 시각과 안정 시작 시각을 분리해서 관리한다.
        // 순간적으로 감지가 끊겨도 바로 녹화를 멈추지 않고, 일정 시간 안정 상태가 이어질 때만 종료한다.
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

        double secondsSinceLastMotion = _lastMotionTime == DateTime.MinValue
            ? double.MaxValue
            : (now - _lastMotionTime).TotalSeconds;
        TimeSpan stableHomeDuration = _stableHomeSince.HasValue ? now - _stableHomeSince.Value : TimeSpan.Zero;

        string triggerReason = GetTriggerReason(result);
        int recordingStopWaitSeconds = _settings.Detection.RecordingStopWaitSeconds;
        // 시작은 수동 요청 또는 자동 감지 트리거가 있을 때만 허용한다.
        // 종료 직후에는 쿨다운을 둬서 같은 이벤트가 여러 파일로 쪼개지는 것을 줄인다.
        bool shouldStartRecording = CurrentState != BlackboxState.Recording
            && now >= _cooldownUntil
            && (_manualRecordingActive || (AutoRecordingEnabled && triggerReason.Length > 0));
        // 녹화 유지 조건은 종료 조건보다 넓게 잡는다.
        // 움직임, 홈 불안정, 정지 대기 시간, 수동 녹화 중 하나라도 남아 있으면 계속 기록한다.
        bool shouldKeepRecording = result.PersonDetected
            || !result.HomeStable
            || secondsSinceLastMotion < recordingStopWaitSeconds
            || _manualRecordingActive;
        // 자동 녹화는 움직임이 사라지고 홈 ROI도 안정된 상태가 충분히 지속되어야 멈춘다.
        bool canStopRecording = !_manualRecordingActive
            && !result.PersonDetected
            && result.HomeStable
            && stableHomeDuration.TotalSeconds >= recordingStopWaitSeconds;
        string recordingHoldReason = GetRecordingHoldReason(
            result,
            recordingStopWaitSeconds,
            secondsSinceLastMotion,
            stableHomeDuration.TotalSeconds,
            _manualRecordingActive);

        bool shouldStopRecording = CurrentState == BlackboxState.Recording && canStopRecording;
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
