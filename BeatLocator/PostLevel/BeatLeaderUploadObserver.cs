using IPA.Loader;
using System;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Zenject;

namespace BeatLocator.PostLevel;

/// <summary>
/// Observes BeatLeader's public upload event without adding a hard assembly
/// reference, so BeatLocator still builds and starts when BeatLeader is absent.
/// </summary>
internal sealed class BeatLeaderUploadObserver : IInitializable, IDisposable
{
    private readonly object _sync = new object();
    private EventInfo? _eventInfo;
    private MethodInfo? _removeListenerMethod;
    private Delegate? _eventHandler;
    private long _armedRunId;
    private TaskCompletionSource<BeatLeaderUploadResult>? _completionSource;

    internal bool UsesLegacyObserver { get; private set; }

    public void Initialize()
    {
        try
        {
            var plugin = PluginManager.GetPluginFromId("BeatLeader");
            var assembly = plugin?.Assembly;
            if (assembly == null)
            {
                Plugin.Log.Warn("[PP] BeatLeader assembly is unavailable.");
                return;
            }

            if (TryAttachModernObserver(assembly) || TryAttachLegacyObserver(assembly))
            {
                return;
            }

            Plugin.Log.Warn("[PP] BeatLeader upload event/listener is unavailable.");
        }
        catch (Exception exception)
        {
            Plugin.Log.Warn($"[PP] Could not attach BeatLeader upload observer: {exception}");
            _eventInfo = null;
            _removeListenerMethod = null;
            _eventHandler = null;
        }
    }

    private bool TryAttachModernObserver(Assembly assembly)
    {
        var requestType = assembly.GetType("BeatLeader.API.UploadReplayRequest");
        _eventInfo = requestType?.GetEvent(
                "StateChangedEvent",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        if (_eventInfo?.EventHandlerType == null) return false;

        _eventHandler = CreateThreeParameterHandler(
            _eventInfo.EventHandlerType,
            nameof(HandleModernStateChanged));
        _eventInfo.AddEventHandler(null, _eventHandler);
        UsesLegacyObserver = false;
        Plugin.Log.Info("[PP] BeatLeader modern upload observer attached.");
        return true;
    }

    private bool TryAttachLegacyObserver(Assembly assembly)
    {
        var requestType = assembly.GetType("BeatLeader.API.Methods.UploadReplayRequest");
        var handlerType = requestType?.BaseType;
        var addListenerMethod = handlerType?.GetMethod(
            "AddStateListener",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        _removeListenerMethod = handlerType?.GetMethod(
            "RemoveStateListener",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        var delegateType = addListenerMethod?.GetParameters().Length == 1
            ? addListenerMethod.GetParameters()[0].ParameterType
            : null;
        if (addListenerMethod == null || _removeListenerMethod == null || delegateType == null)
        {
            _removeListenerMethod = null;
            return false;
        }

        _eventHandler = CreateThreeParameterHandler(
            delegateType,
            nameof(HandleLegacyStateChanged));
        addListenerMethod.Invoke(null, new object[] { _eventHandler });
        UsesLegacyObserver = true;
        Plugin.Log.Info("[PP] BeatLeader 0.9.x upload observer attached.");
        return true;
    }

    private Delegate CreateThreeParameterHandler(Type delegateType, string callbackName)
    {
        var invokeMethod = delegateType.GetMethod("Invoke")
            ?? throw new MissingMethodException(delegateType.Name, "Invoke");
        var parameters = invokeMethod.GetParameters();
        if (parameters.Length != 3)
        {
            throw new InvalidOperationException(
                $"Unexpected BeatLeader upload callback signature ({parameters.Length} parameters).");
        }

        var firstParameter = Expression.Parameter(parameters[0].ParameterType, "first");
        var secondParameter = Expression.Parameter(parameters[1].ParameterType, "second");
        var reasonParameter = Expression.Parameter(parameters[2].ParameterType, "reason");
        var callback = GetType().GetMethod(
            callbackName,
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException(GetType().Name, callbackName);
        var body = Expression.Call(
            Expression.Constant(this),
            callback,
            Expression.Convert(firstParameter, typeof(object)),
            Expression.Convert(secondParameter, typeof(object)),
            Expression.Convert(reasonParameter, typeof(string)));
        return Expression.Lambda(
            delegateType,
            body,
            firstParameter,
            secondParameter,
            reasonParameter).Compile();
    }

    internal Task<BeatLeaderUploadResult>? Arm(long runId)
    {
        lock (_sync)
        {
            if ((_eventInfo == null && _removeListenerMethod == null) || _eventHandler == null)
            {
                Plugin.Log.Warn(
                    $"[PP] BeatLeader run {runId} cannot observe score upload; " +
                    "the upload event is not attached.");
                return null;
            }

            _armedRunId = runId;
            _completionSource?.TrySetCanceled();
            _completionSource = new TaskCompletionSource<BeatLeaderUploadResult>();
            return _completionSource.Task;
        }
    }

    internal void Disarm(long runId)
    {
        lock (_sync)
        {
            if (_armedRunId != runId) return;
            _armedRunId = 0;
            _completionSource?.TrySetCanceled();
            _completionSource = null;
        }
    }

    private void HandleModernStateChanged(object? request, object? state, string? failReason)
    {
        var result = GetMemberValue(request, "Result");
        var stateName = state?.ToString() ?? string.Empty;
        var status = GetMemberValue(result, "Status")?.ToString() ?? string.Empty;
        var detail = GetMemberValue(result, "Description")?.ToString() ?? failReason;

        // BeatLeader 0.9.34 exposes the modern StateChangedEvent, but its
        // successful Result is BeatLeader.Models.Score and has no Status or
        // Description member. Finished is therefore only an upload-completion
        // signal; the public score API remains the source of truth for PB/PP.
        if (string.Equals(stateName, "Finished", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(status))
        {
            status = "FinishedWithoutStatus";
            var scoreId = GetMemberValue(result, "id")?.ToString() ??
                          GetMemberValue(result, "Id")?.ToString();
            detail = !string.IsNullOrWhiteSpace(scoreId)
                ? $"compatibility_score_id={scoreId}; payload={result?.GetType().FullName ?? "<null>"}"
                : $"payload={result?.GetType().FullName ?? "<null>"}";
        }

        CompleteTerminalState(
            state,
            status,
            detail);
    }

    private void HandleLegacyStateChanged(object? state, object? result, string? failReason)
    {
        var stateName = state?.ToString() ?? string.Empty;
        var status = string.Equals(stateName, "Finished", StringComparison.OrdinalIgnoreCase)
            ? "UploadedLegacy"
            : string.Empty;
        var scoreId = GetMemberValue(result, "id")?.ToString();
        CompleteTerminalState(
            state,
            status,
            !string.IsNullOrWhiteSpace(scoreId) ? $"legacy_score_id={scoreId}" : failReason);
    }

    private void CompleteTerminalState(object? state, string status, string? detail)
    {
        var stateName = state?.ToString() ?? string.Empty;
        if (!string.Equals(stateName, "Finished", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(stateName, "Failed", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        TaskCompletionSource<BeatLeaderUploadResult>? completionSource;
        long runId;
        lock (_sync)
        {
            if (_armedRunId == 0 || _completionSource == null) return;
            runId = _armedRunId;
            completionSource = _completionSource;
            _armedRunId = 0;
            _completionSource = null;
        }

        Plugin.Log.Info(
            $"[PP] BeatLeader upload observer completed run {runId}: " +
            $"state={stateName}, status={status}, detail={detail ?? "<none>"}.");

        completionSource.TrySetResult(new BeatLeaderUploadResult
        {
            State = stateName,
            Status = status,
            Detail = detail ?? string.Empty
        });
    }

    private static object? GetMemberValue(object? instance, string name)
    {
        if (instance == null) return null;
        var type = instance.GetType();
        return type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
                   ?.GetValue(instance, null)
               ?? type.GetField(name, BindingFlags.Public | BindingFlags.Instance)
                   ?.GetValue(instance);
    }

    public void Dispose()
    {
        if (_eventInfo != null && _eventHandler != null)
        {
            _eventInfo.RemoveEventHandler(null, _eventHandler);
        }
        else if (_removeListenerMethod != null && _eventHandler != null)
        {
            _removeListenerMethod.Invoke(null, new object[] { _eventHandler });
        }

        lock (_sync)
        {
            _completionSource?.TrySetCanceled();
            _completionSource = null;
            _armedRunId = 0;
            UsesLegacyObserver = false;
        }
    }
}
