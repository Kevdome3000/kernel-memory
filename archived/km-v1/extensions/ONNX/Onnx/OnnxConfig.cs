// Copyright (c) Microsoft.All rights reserved.

using System;
using System.IO;
using System.Linq;

#pragma warning disable IDE0130 // reduce number of "using" statements

#pragma warning disable IDE0130 // reduce number of "using" statements
// ReSharper disable once CheckNamespace - reduce number of "using" statements
namespace Microsoft.KernelMemory;

public class OnnxConfig
{
    /// <summary>
    /// An enum representing the possible text generation search types used by OnnxTextGenerator.
    /// See https://onnxruntime.ai/docs/genai/reference/config.html#search-combinations for more details.
    /// </summary>
    public enum OnnxSearchType
    {
        /// <summary>
        /// A decoding algorithm that keeps track of the top K sequences at each step. It explores
        /// multiple paths simultaneously, balancing exploration and exploitation. Often results in more
        /// coherent and higher quality text generation than Greedy Search would.
        /// </summary>
        BeamSearch,

        /// <summary>
        /// The default and simplest decoding algorithm. At each step, a token is selected with the highest
        /// probability as the next word in the sequence.
        /// </summary>
        GreedySearch,

        /// <summary>
        /// Combined Top-P (Nucleus) and Top-K Sampling: A decoding algorithm that samples from the top k tokens
        /// with the highest probabilities, while also considering the smallest set of tokens whose cumulative
        /// probability exceeds a threshold p. This approach dynamically balances diversity and coherence in
        /// text generation by adjusting the sampling pool based on both fixed and cumulative probability criteria.
        /// </summary>
        TopN
    }


    /// <summary>
    /// Path to the directory containing the .ONNX file for Text Generation.
    /// </summary>
    public string TextModelDir { get; set; } = string.Empty;

    /// <summary>
    /// The maximum length of the response that the model will generate. See https://onnxruntime.ai/docs/genai/reference/config.html
    /// </summary>
    public int MaxTokens { get; set; } = 2048;

    /// <summary>
    /// Name of the tokenizer used to count tokens.
    /// Supported values: "p50k", "cl100k", "o200k". Leave it empty if unsure.
    /// </summary>
    public string Tokenizer { get; set; } = "o200k";

    /// <summary>
    /// The minimum length of the response that the model will generate. See https://onnxruntime.ai/docs/genai/reference/config.html
    /// </summary>
    public uint MinLength { get; set; } = 0;

    /// <summary>
    /// The algorithm used in text generation. Defaults to GreedySearch.
    /// </summary>
    public OnnxSearchType SearchType { get; set; } = OnnxSearchType.GreedySearch;

    /// <summary>
    /// The number of beams to apply when generating the output sequence using beam search.
    /// If NumBeams=1, then generation is performed using greedy search. If NumBeans > 1, then
    /// generation is performed using beam search. A null value implies using TopN search.
    /// </summary>
    public uint? NumBeams { get; set; } = 1;

    /// <summary>
    /// Only includes the most probable tokens with probabilities that add up to P or higher.
    /// Defaults to 1, which includes all of the tokens. Range is 0 to 1, exclusive of 0.
    /// </summary>
    public double NucleusSampling { get; set; } = 1.0;

    /// <summary>
    /// Whether to stop the beam search when at least NumBeams sentences are finished per batch or not. Defaults to false.
    /// </summary>
    public bool EarlyStopping { get; set; } = false;

    /// <summary>
    /// The number of sequences (responses) to generate. Returns the sequences with the highest scores in order.
    /// </summary>
    public int ResultsPerPrompt { get; set; } = 1;

    /// <summary>
    /// Only includes tokens that fall within the list of the K most probable tokens. Range is 1 to the vocabulary size.
    /// Defaults to 50.
    /// </summary>
    public uint TopK { get; set; } = 50;

    /// <summary>
    /// Discounts the scores of previously generated tokens if set to a value greater than 1.
    /// Defaults to 1.
    /// </summary>
    public double RepetitionPenalty { get; set; } = 1.0;

    /// <summary>
    /// Controls the length of the output generated. Value less than 1 encourages the generation
    /// to produce shorter sequences. Values greater than 1 encourages longer sequences. Defaults to 1.
    /// </summary>
    public double LengthPenalty { get; set; } = 1.0;


    /// <summary>
    /// Verify that the current state is valid.
    /// </summary>
    public void Validate(bool allowIO = true)
    {
        if (string.IsNullOrEmpty(TextModelDir))
        {
            throw new ConfigurationException($"Onnx: {nameof(TextModelDir)} is a required field.");
        }

        var modelDir = Path.GetFullPath(TextModelDir);

        if (allowIO)
        {
            if (!Directory.Exists(modelDir))
            {
                throw new ConfigurationException($"Onnx: {TextModelDir} does not exist.");
            }

            if (Directory.GetFiles(modelDir).Length == 0)
            {
                throw new ConfigurationException($"Onnx: {TextModelDir} is an empty directory.");
            }

            var modelFiles = Directory.GetFiles(modelDir)
                .Where(file => string.Equals(Path.GetExtension(file), ".ONNX", StringComparison.OrdinalIgnoreCase));

            if (modelFiles == null)
            {
                throw new ConfigurationException($"Onnx: {TextModelDir} does not contain a valid .ONNX model.");
            }
        }

        if (SearchType == OnnxSearchType.GreedySearch)
        {
            if (NumBeams != 1)
            {
                throw new ConfigurationException($"Onnx: {nameof(NumBeams)} is only used with Beam Search. Change {nameof(NumBeams)} to 1, or change {nameof(SearchType)} to BeamSearch.");
            }

            if (EarlyStopping)
            {
                throw new ConfigurationException($"Onnx: {nameof(EarlyStopping)} is only used with Beam Search. Change {nameof(EarlyStopping)} to false, or change {nameof(SearchType)} to BeamSearch.");
            }
        }

        if (SearchType == OnnxSearchType.BeamSearch)
        {
            if (NumBeams == null)
            {
                throw new ConfigurationException($"Onnx: {nameof(NumBeams)} is required for Beam Search. Change {nameof(NumBeams)} to a value >= 1, or change the {nameof(SearchType)}.");
            }
        }

        if (SearchType == OnnxSearchType.TopN)
        {
            if (NumBeams != null)
            {
                throw new ConfigurationException($"Onnx: {nameof(NumBeams)} isn't required with TopN Search. Change {nameof(NumBeams)} to null, or change the {nameof(SearchType)}.");
            }

            if (EarlyStopping)
            {
                throw new ConfigurationException($"Onnx: {nameof(EarlyStopping)} is only used with Beam Search. Change {nameof(EarlyStopping)} to false, or change {nameof(SearchType)} to BeamSearch.");
            }
        }
    }
}
