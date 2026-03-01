// Copyright (c) Microsoft.All rights reserved.

using System.Collections.Generic;
using System.Linq;

namespace Microsoft.KernelMemory.MemoryDb.AzureAISearch.Internals;

internal sealed class MemoryDbSchema
{
    public List<MemoryDbField> Fields { get; set; } = [];


    public void Validate(bool vectorSizeRequired = false)
    {
        if (Fields.Count == 0)
        {
            throw new AzureAISearchMemoryException("The schema is empty", false);
        }

        if (Fields.All(x => x.Type != MemoryDbField.FieldType.Vector))
        {
            throw new AzureAISearchMemoryException("The schema doesn't contain a vector field", false);
        }

        int keys = Fields.Count(x => x.IsKey);

        switch (keys)
        {
            case 0:
                throw new AzureAISearchMemoryException("The schema doesn't contain a key field", false);
            case > 1:
                throw new AzureAISearchMemoryException("The schema cannot contain more than one key", false);
        }

        if (vectorSizeRequired && Fields.Any(x => x is { Type: MemoryDbField.FieldType.Vector, VectorSize: 0 }))
        {
            throw new AzureAISearchMemoryException("Vector fields must have a size greater than zero defined", false);
        }

        if (Fields.Any(x => x is { Type: MemoryDbField.FieldType.Bool, IsKey: true }))
        {
            throw new AzureAISearchMemoryException("Boolean fields cannot be used as unique keys", false);
        }

        if (Fields.Any(x => x is { Type: MemoryDbField.FieldType.ListOfStrings, IsKey: true }))
        {
            throw new AzureAISearchMemoryException("Collection fields cannot be used as unique keys", false);
        }

        if (Fields.Any(x => x is { Type: MemoryDbField.FieldType.Vector, IsKey: true }))
        {
            throw new AzureAISearchMemoryException("Vector fields cannot be used as unique keys", false);
        }
    }
}
