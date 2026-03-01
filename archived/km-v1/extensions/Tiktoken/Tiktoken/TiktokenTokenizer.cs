// Copyright (c) Microsoft.All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.Tokenizers;

namespace Microsoft.KernelMemory.AI;

public class TiktokenTokenizer : ITextTokenizer
{
    private readonly Tokenizer _tokenizer;


    public TiktokenTokenizer(string modelId)
    {
        try
        {
            _tokenizer = ML.Tokenizers.TiktokenTokenizer.CreateForModel(modelId);
        }
        catch (NotSupportedException)
        {
            throw new KernelMemoryException("Autodetect failed");
        }
        catch (ArgumentNullException)
        {
            throw new KernelMemoryException("Autodetect failed");
        }
    }


    public int CountTokens(string text)
    {
        return _tokenizer.CountTokens(text);
    }


    public IReadOnlyList<string> GetTokens(string text)
    {
        return _tokenizer.EncodeToTokens(text, out string? _).Select(t => t.Value).ToList();
    }
}
