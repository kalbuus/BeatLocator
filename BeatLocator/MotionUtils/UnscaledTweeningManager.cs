using Tweening;
using UnityEngine;

namespace MotionUtils;

internal sealed class UnscaledTweeningManager : TweeningManager
{
    private bool _initialized;

    private void Awake()
    {
        EnsureInitialized();
    }

    private new void Start()
    {
        EnsureInitialized();
    }

    protected override float GetTime()
    {
        return Time.unscaledTime;
    }

    private void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        base.Start();
        _initialized = true;
    }
}
