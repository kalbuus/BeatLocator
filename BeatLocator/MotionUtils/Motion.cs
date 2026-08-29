using UnityEngine;

namespace MotionUtils;

internal static class Motion
{
    internal static MotionScope For(Component owner)
    {
        foreach (var scope in owner.GetComponents<MotionScope>())
        {
            if (scope.IsOwnedBy(owner))
            {
                return scope;
            }
        }

        var newScope = owner.gameObject.AddComponent<MotionScope>();
        newScope.Bind(owner);
        return newScope;
    }
}
