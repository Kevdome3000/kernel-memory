// Copyright (c) Microsoft.All rights reserved.

using System.Collections.Generic;
using System.Linq;
using LLama;

namespace Microsoft.KernelMemory.AI.LlamaSharp;

public sealed class LlamaSharpTokenizer : ITextTokenizer
{
    // Whether to prepend a BoS (Beginning of Sequence) token to the text.
    private const bool AddBos = false;

    // Allow tokenizing special and/ or control tokens which otherwise are not exposed and treated as plaintext.
    private const bool Special = true;

    private readonly LLamaContext _context;


    public LlamaSharpTokenizer(LLamaContext context)
    {
        _context = context;
    }


    public int CountTokens(string text)
    {
        return _context.Tokenize(text, AddBos, Special).Length;
    }


    public IReadOnlyList<string> GetTokens(string text)
    {
        StreamingTokenDecoder decoder = new(_context);
        return _context.Tokenize(text, AddBos, Special)
            .Select(x =>
            {
                decoder.Add(x);
                return decoder.Read();
            })
            .ToList();
    }
}
