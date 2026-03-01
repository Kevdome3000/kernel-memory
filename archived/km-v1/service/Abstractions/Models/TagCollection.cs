// Copyright (c) Microsoft.All rights reserved.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Microsoft.KernelMemory.Models;

// JSON serializable alternative to NameValueCollection
public class TagCollection : IDictionary<string, List<string?>>
{
    private readonly IDictionary<string, List<string?>> _data = new Dictionary<string, List<string?>>(StringComparer.OrdinalIgnoreCase);

    public ICollection<string> Keys => _data.Keys;

    public ICollection<List<string?>> Values => _data.Values;

    public IEnumerable<KeyValuePair<string, string?>> Pairs => from key in _data.Keys
                                                               from value in _data[key]
                                                               select new KeyValuePair<string, string?>(key, value);

    public int Count => _data.Count;

    public bool IsReadOnly => _data.IsReadOnly;

    public List<string?> this[string key]
    {
        get => _data[key];
        set
        {
            ValidateKey(key);
            _data[key] = value;
        }
    }


    public IEnumerator<KeyValuePair<string, List<string?>>> GetEnumerator()
    {
        return _data.GetEnumerator();
    }


    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }


    public void Add(KeyValuePair<string, List<string?>> item)
    {
        ValidateKey(item.Key);
        _data.Add(item);
    }


    public void Add(string key)
    {
        if (!_data.ContainsKey(key))
        {
            _data[key] = [];
        }
    }


    public void Add(string key, string? value)
    {
        ValidateKey(key);

        // If the key exists
        if (_data.TryGetValue(key, out List<string?>? list) && list != null)
        {
            if (value != null) { list.Add(value); }
        }
        else
        {
            // Add the key, but the value only if not null
            _data[key] = value == null
                ? []
                : [value];
        }
    }


    public void Add(string key, List<string?> value)
    {
        ValidateKey(key);
        _data.Add(key, value);
    }


    public bool TryGetValue(string key, out List<string?> value)
    {
        bool result = _data.TryGetValue(key, out var valueOut);
        value = valueOut ?? [];
        return result;
    }


    public bool Contains(KeyValuePair<string, List<string?>> item)
    {
        return _data.Contains(item);
    }


    public bool ContainsKey(string key)
    {
        return _data.ContainsKey(key);
    }


    public void CopyTo(KeyValuePair<string, List<string?>>[] array, int arrayIndex)
    {
        _data.CopyTo(array, arrayIndex);
    }


    public void CopyTo(TagCollection tagCollection)
    {
        foreach (string key in _data.Keys)
        {
            if (_data[key] == null || _data[key].Count == 0)
            {
                tagCollection.Add(key);
            }
            else
            {
                foreach (string? value in _data[key])
                {
                    tagCollection.Add(key, value);
                }
            }
        }
    }


    public IEnumerable<KeyValuePair<string, string?>> ToKeyValueList()
    {
        return from tag in _data from tagValue in tag.Value select new KeyValuePair<string, string?>(tag.Key, tagValue);
    }


    public bool Remove(KeyValuePair<string, List<string?>> item)
    {
        return _data.Remove(item);
    }


    public bool Remove(string key)
    {
        return _data.Remove(key);
    }


    public void Clear()
    {
        _data.Clear();
    }


    public override string ToString()
    {
        return ToString(_data.Where(x => x.Value.Count > 0));
    }


    public string ToStringExcludeReserved()
    {
        return ToString(_data.Where(x => x.Value.Count > 0 && !x.Key.StartsWith(Constants.ReservedTagsPrefix, StringComparison.Ordinal)));
    }


    private static string ToString(IEnumerable<KeyValuePair<string, List<string?>>> list)
    {
        var result = new StringBuilder();

        foreach (KeyValuePair<string, List<string?>> tags in list)
        {
            if (tags.Value.Count == 1)
            {
                result.Append(tags.Key).Append(':').Append(tags.Value.First());
            }
            else
            {
                result.Append(tags.Key).Append(":[").Append(string.Join(", ", tags.Value)).Append(']');
            }

            result.Append(';');
        }

        return result.ToString().TrimEnd(';');
    }


    private static void ValidateKey(string key)
    {
        if (key.Contains(Constants.ReservedEqualsChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new KernelMemoryException($"A tag name cannot contain the '{Constants.ReservedEqualsChar}' char");
        }

        // '=' is reserved for backward/forward compatibility and to reduce URLs query params encoding complexity
        if (key.Contains('=', StringComparison.OrdinalIgnoreCase))
        {
            throw new KernelMemoryException("A tag name cannot contain the '=' char");
        }

        // ':' is reserved for backward/forward compatibility
        if (key.Contains(':', StringComparison.OrdinalIgnoreCase))
        {
            throw new KernelMemoryException("A tag name cannot contain the ':' char");
        }
    }
}
