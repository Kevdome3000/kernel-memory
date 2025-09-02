// Copyright (c) Microsoft.All rights reserved.

namespace Microsoft.KernelMemory.InteractiveSetup.UI;

/// <summary>
/// A boolean that can "change" to True only a limited number of times
/// </summary>
public sealed class BoundedBoolean
{
    private readonly int _maxChangesToTrue;
    private int _changesToTrueCount;
    private bool _value;

    public bool Value
    {
        get => _value;
        set
        {
            if (!value)
            {
                _value = false;
                return;
            }

            if (_changesToTrueCount < _maxChangesToTrue)
            {
                _changesToTrueCount++;
                _value = true;
            }
        }
    }


    public BoundedBoolean(bool initialState = false, int maxChangesToTrue = 1)
    {
        _changesToTrueCount = 0;
        _maxChangesToTrue = maxChangesToTrue;
        _value = initialState;
    }
}
