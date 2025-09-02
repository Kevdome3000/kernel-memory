// Copyright (c) Microsoft.All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.AI;
using Microsoft.KernelMemory.AI.Anthropic;
using Microsoft.KernelMemory.AI.Ollama;
using Microsoft.KernelMemory.DocumentStorage.DevTools;
using Microsoft.KernelMemory.MemoryDb.SQLServer;
using Microsoft.KernelMemory.MemoryStorage;
using Microsoft.KernelMemory.MemoryStorage.DevTools;
using Microsoft.KernelMemory.MongoDbAtlas;
using Microsoft.KernelMemory.Pipeline.Queue.DevTools;
using Microsoft.KernelMemory.Safety.AzureAIContentSafety;

namespace Microsoft.KernelMemory.Internals;

/// <summary>
/// Meta factory class responsible for configuring IKernelMemoryBuilder
/// with the components selected in the configuration.
/// </summary>
internal sealed class KernelMemoryComposer
{
    // appsettings.json root node name (and prefix of env vars)
    public const string ConfigRoot = "KernelMemory";


    public KernelMemoryComposer(
        IKernelMemoryBuilder builder,
        IConfiguration globalSettings,
        KernelMemoryConfig memoryConfiguration)
    {
        _builder = builder;
        _globalSettings = globalSettings;
        _memoryConfiguration = memoryConfiguration;

        if (!MinimumConfigurationIsAvailable(false)) { SetupForOpenAI(); }

        MinimumConfigurationIsAvailable(true);
    }


    public void ConfigureBuilder()
    {
        if (_memoryConfiguration == null)
        {
            throw new ConfigurationException("The given memory configuration is NULL");
        }

        if (_globalSettings == null)
        {
            throw new ConfigurationException("The given app settings configuration is NULL");
        }

        // Required by ctors expecting KernelMemoryConfig via DI
        _builder.AddSingleton<KernelMemoryConfig>(_memoryConfiguration);

        ConfigureMimeTypeDetectionDependency();

        ConfigureTextPartitioning();

        ConfigureQueueDependency();

        ConfigureStorageDependency();

        // The ingestion embedding generators is a list of generators that the "gen_embeddings" handler uses,
        // to generate embeddings for each partition. While it's possible to use multiple generators (e.g. to compare embedding quality)
        // only one generator is used when searching by similarity, and the generator used for search is not in this list.
        // - config.DataIngestion.EmbeddingGeneratorTypes => list of generators, embeddings to generate and store in memory DB
        // - config.Retrieval.EmbeddingGeneratorType      => one embedding generator, used to search, and usually injected into Memory DB constructor

        ConfigureIngestionEmbeddingGenerators();

        ConfigureContentModeration();

        ConfigureSearchClient();

        ConfigureRetrievalEmbeddingGenerator();

        // The ingestion Memory DBs is a list of DBs where handlers write records to. While it's possible
        // to write to multiple DBs, e.g. for replication purpose, there is only one Memory DB used to
        // read/search, and it doesn't come from this list. See "config.Retrieval.MemoryDbType".
        // Note: use the aux service collection to avoid mixing ingestion and retrieval dependencies.

        ConfigureIngestionMemoryDb();

        ConfigureRetrievalMemoryDb();

        ConfigureTextGenerator();

        ConfigureImageOCR();
    }


    #region private ===============================

    // Builder to be configured with the required components
    private readonly IKernelMemoryBuilder _builder;

    // Content of appsettings.json, used to access dynamic data under "Services"
    private IConfiguration _globalSettings;

    // Normalized configuration
    private KernelMemoryConfig _memoryConfiguration;

    // ASP.NET env var
    private const string AspnetEnvVar = "ASPNETCORE_ENVIRONMENT";

    // OpenAI env var
    private const string OpenAIEnvVar = "OPENAI_API_KEY";


    private void ConfigureQueueDependency()
    {
        if (string.Equals(_memoryConfiguration.DataIngestion.OrchestrationType, KernelMemoryConfig.OrchestrationTypeDistributed, StringComparison.OrdinalIgnoreCase))
        {
            switch (_memoryConfiguration.DataIngestion.DistributedOrchestration.QueueType)
            {
                case { } y1 when y1.Equals("AzureQueue", StringComparison.OrdinalIgnoreCase):
                case { } y2 when y2.Equals("AzureQueues", StringComparison.OrdinalIgnoreCase):
                    // Check 2 keys for backward compatibility
                    _builder.Services.AddAzureQueuesOrchestration(GetServiceConfig<AzureQueuesConfig>("AzureQueues")
                        ?? GetServiceConfig<AzureQueuesConfig>("AzureQueue"));
                    break;

                case { } y when y.Equals("RabbitMQ", StringComparison.OrdinalIgnoreCase):
                    // Check 2 keys for backward compatibility
                    _builder.Services.AddRabbitMQOrchestration(GetServiceConfig<RabbitMQConfig>("RabbitMQ")
                        ?? GetServiceConfig<RabbitMQConfig>("RabbitMq"));
                    break;

                case { } y when y.Equals("SimpleQueues", StringComparison.OrdinalIgnoreCase):
                    _builder.Services.AddSimpleQueues(GetServiceConfig<SimpleQueuesConfig>("SimpleQueues"));
                    break;

            }
        }
    }


    private void ConfigureStorageDependency()
    {
        switch (_memoryConfiguration.DocumentStorageType)
        {
            case { } x1 when x1.Equals("AzureBlob", StringComparison.OrdinalIgnoreCase):
            case { } x2 when x2.Equals("AzureBlobs", StringComparison.OrdinalIgnoreCase):
                // Check 2 keys for backward compatibility
                _builder.Services.AddAzureBlobsAsDocumentStorage(GetServiceConfig<AzureBlobsConfig>("AzureBlobs")
                    ?? GetServiceConfig<AzureBlobsConfig>("AzureBlob"));
                break;

            case { } x when x.Equals("AWSS3", StringComparison.OrdinalIgnoreCase):
                _builder.Services.AddAWSS3AsDocumentStorage(GetServiceConfig<AWSS3Config>("AWSS3"));
                break;

            case { } x when x.Equals("MongoDbAtlas", StringComparison.OrdinalIgnoreCase):
                _builder.Services.AddMongoDbAtlasAsDocumentStorage(GetServiceConfig<MongoDbAtlasConfig>("MongoDbAtlas"));
                break;

            case { } x when x.Equals("SimpleFileStorage", StringComparison.OrdinalIgnoreCase):
                _builder.Services.AddSimpleFileStorageAsDocumentStorage(GetServiceConfig<SimpleFileStorageConfig>("SimpleFileStorage"));
                break;

        }
    }


    private void ConfigureTextPartitioning()
    {
        if (_memoryConfiguration.DataIngestion.TextPartitioning != null)
        {
            _memoryConfiguration.DataIngestion.TextPartitioning.Validate();
            _builder.WithCustomTextPartitioningOptions(_memoryConfiguration.DataIngestion.TextPartitioning);
        }
    }


    private void ConfigureMimeTypeDetectionDependency()
    {
        _builder.WithDefaultMimeTypeDetection();
    }


    private void ConfigureIngestionEmbeddingGenerators()
    {
        // Note: using multiple embeddings is not fully supported yet and could cause write errors or incorrect search results
        if (_memoryConfiguration.DataIngestion.EmbeddingGeneratorTypes.Count > 1)
        {
            throw new NotSupportedException("Using multiple embedding generators is currently unsupported. " + "You may contact the team if this feature is required, or workaround this exception " + "using KernelMemoryBuilder methods explicitly.");
        }

        foreach (var type in _memoryConfiguration.DataIngestion.EmbeddingGeneratorTypes)
        {
            switch (type)
            {
                case { } x when x.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase):
                case { } y when y.Equals("AzureOpenAIEmbedding", StringComparison.OrdinalIgnoreCase):
                {
                    var instance = GetServiceInstance<ITextEmbeddingGenerator>(s => s.AddAzureOpenAIEmbeddingGeneration(
                        GetServiceConfig<AzureOpenAIConfig>("AzureOpenAIEmbedding")));
                    _builder.AddIngestionEmbeddingGenerator(instance);
                    break;
                }

                case { } x when x.Equals("OpenAI", StringComparison.OrdinalIgnoreCase):
                {
                    var instance = GetServiceInstance<ITextEmbeddingGenerator>(s => s.AddOpenAITextEmbeddingGeneration(
                        GetServiceConfig<OpenAIConfig>("OpenAI")));
                    _builder.AddIngestionEmbeddingGenerator(instance);
                    break;
                }

                case { } x when x.Equals("Ollama", StringComparison.OrdinalIgnoreCase):
                {
                    var instance = GetServiceInstance<ITextEmbeddingGenerator>(s => s.AddOllamaTextEmbeddingGeneration(
                        GetServiceConfig<OllamaConfig>("Ollama")));
                    _builder.AddIngestionEmbeddingGenerator(instance);
                    break;
                }

                case { } x when x.Equals("LlamaSharp", StringComparison.OrdinalIgnoreCase):
                {
                    var instance = GetServiceInstance<ITextEmbeddingGenerator>(s => s.AddLlamaSharpTextEmbeddingGeneration(
                        GetServiceConfig<LlamaSharpConfig>("LlamaSharp").EmbeddingModel));
                    _builder.AddIngestionEmbeddingGenerator(instance);
                    break;
                }

            }
        }
    }


    private void ConfigureIngestionMemoryDb()
    {
        foreach (var type in _memoryConfiguration.DataIngestion.MemoryDbTypes)
        {
            switch (type)
            {
                default:
                    throw new ConfigurationException(
                        $"Unknown Memory DB option '{type}'. " + "To use a custom Memory DB, set the configuration value to an empty string, " + "and inject the custom implementation using `IKernelMemoryBuilder.WithCustomMemoryDb(...)`");

                case "":
                    // NOOP - allow custom implementations, via WithCustomMemoryDb()
                    break;

                case { } x when x.Equals("AzureAISearch", StringComparison.OrdinalIgnoreCase):
                {
                    var instance = GetServiceInstance<IMemoryDb>(s => s.AddAzureAISearchAsMemoryDb(GetServiceConfig<AzureAISearchConfig>("AzureAISearch"))
                    );
                    _builder.AddIngestionMemoryDb(instance);
                    break;
                }

                case { } x when x.Equals("Elasticsearch", StringComparison.OrdinalIgnoreCase):
                {
                    var instance = GetServiceInstance<IMemoryDb>(s => s.AddElasticsearchAsMemoryDb(GetServiceConfig<ElasticsearchConfig>("Elasticsearch"))
                    );
                    _builder.AddIngestionMemoryDb(instance);
                    break;
                }

                case { } x when x.Equals("MongoDbAtlas", StringComparison.OrdinalIgnoreCase):
                {
                    var instance = GetServiceInstance<IMemoryDb>(s => s.AddMongoDbAtlasAsMemoryDb(GetServiceConfig<MongoDbAtlasConfig>("MongoDbAtlas"))
                    );
                    _builder.AddIngestionMemoryDb(instance);
                    break;
                }

                case { } x when x.Equals("Postgres", StringComparison.OrdinalIgnoreCase):
                {
                    var instance = GetServiceInstance<IMemoryDb>(s => s.AddPostgresAsMemoryDb(GetServiceConfig<PostgresConfig>("Postgres"))
                    );
                    _builder.AddIngestionMemoryDb(instance);
                    break;
                }

                case { } x when x.Equals("Qdrant", StringComparison.OrdinalIgnoreCase):
                {
                    var instance = GetServiceInstance<IMemoryDb>(s => s.AddQdrantAsMemoryDb(GetServiceConfig<QdrantConfig>("Qdrant"))
                    );
                    _builder.AddIngestionMemoryDb(instance);
                    break;
                }

                case { } x when x.Equals("Redis", StringComparison.OrdinalIgnoreCase):
                {
                    var instance = GetServiceInstance<IMemoryDb>(s => s.AddRedisAsMemoryDb(GetServiceConfig<RedisConfig>("Redis"))
                    );
                    _builder.AddIngestionMemoryDb(instance);
                    break;
                }

                case { } x when x.Equals("SimpleVectorDb", StringComparison.OrdinalIgnoreCase):
                {
                    var instance = GetServiceInstance<IMemoryDb>(s => s.AddSimpleVectorDbAsMemoryDb(GetServiceConfig<SimpleVectorDbConfig>("SimpleVectorDb"))
                    );
                    _builder.AddIngestionMemoryDb(instance);
                    break;
                }

                case { } x when x.Equals("SimpleTextDb", StringComparison.OrdinalIgnoreCase):
                {
                    var instance = GetServiceInstance<IMemoryDb>(s => s.AddSimpleTextDbAsMemoryDb(GetServiceConfig<SimpleTextDbConfig>("SimpleTextDb"))
                    );
                    _builder.AddIngestionMemoryDb(instance);
                    break;
                }

                case { } x when x.Equals("SqlServer", StringComparison.OrdinalIgnoreCase):
                {
                    var instance = GetServiceInstance<IMemoryDb>(s => s.AddSqlServerAsMemoryDb(GetServiceConfig<SqlServerConfig>("SqlServer"))
                    );
                    _builder.AddIngestionMemoryDb(instance);
                    break;
                }
            }
        }
    }


    private void ConfigureContentModeration()
    {
        switch (_memoryConfiguration.ContentModerationType)
        {
            case { } x when x.Equals("AzureAIContentSafety", StringComparison.OrdinalIgnoreCase):
                _builder.Services.AddAzureAIContentSafetyModeration(GetServiceConfig<AzureAIContentSafetyConfig>("AzureAIContentSafety"));
                break;

        }
    }


    private void ConfigureSearchClient()
    {
        // Search settings
        _builder.WithSearchClientConfig(_memoryConfiguration.Retrieval.SearchClient);
    }


    private void ConfigureRetrievalEmbeddingGenerator()
    {
        // Retrieval embeddings - ITextEmbeddingGeneration interface
        switch (_memoryConfiguration.Retrieval.EmbeddingGeneratorType)
        {
            case { } x when x.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase):
            case { } y when y.Equals("AzureOpenAIEmbedding", StringComparison.OrdinalIgnoreCase):
                _builder.Services.AddAzureOpenAIEmbeddingGeneration(
                    GetServiceConfig<AzureOpenAIConfig>("AzureOpenAIEmbedding"));
                break;

            case { } x when x.Equals("OpenAI", StringComparison.OrdinalIgnoreCase):
                _builder.Services.AddOpenAITextEmbeddingGeneration(
                    GetServiceConfig<OpenAIConfig>("OpenAI"));
                break;

            case { } x when x.Equals("Ollama", StringComparison.OrdinalIgnoreCase):
                _builder.Services.AddOllamaTextEmbeddingGeneration(
                    GetServiceConfig<OllamaConfig>("Ollama"));
                break;

            case { } x when x.Equals("LlamaSharp", StringComparison.OrdinalIgnoreCase):
                _builder.Services.AddLlamaSharpTextEmbeddingGeneration(
                    GetServiceConfig<LlamaSharpConfig>("LlamaSharp").EmbeddingModel);
                break;

        }
    }


    private void ConfigureRetrievalMemoryDb()
    {
        // Retrieval Memory DB - IMemoryDb interface
        switch (_memoryConfiguration.Retrieval.MemoryDbType)
        {
            case { } x when x.Equals("AzureAISearch", StringComparison.OrdinalIgnoreCase):
                _builder.Services.AddAzureAISearchAsMemoryDb(GetServiceConfig<AzureAISearchConfig>("AzureAISearch"));
                break;

            case { } x when x.Equals("Elasticsearch", StringComparison.OrdinalIgnoreCase):
                _builder.Services.AddElasticsearchAsMemoryDb(GetServiceConfig<ElasticsearchConfig>("Elasticsearch"));
                break;

            case { } x when x.Equals("MongoDbAtlas", StringComparison.OrdinalIgnoreCase):
                _builder.Services.AddMongoDbAtlasAsMemoryDb(GetServiceConfig<MongoDbAtlasConfig>("MongoDbAtlas"));
                break;

            case { } x when x.Equals("Postgres", StringComparison.OrdinalIgnoreCase):
                _builder.Services.AddPostgresAsMemoryDb(GetServiceConfig<PostgresConfig>("Postgres"));
                break;

            case { } x when x.Equals("Qdrant", StringComparison.OrdinalIgnoreCase):
                _builder.Services.AddQdrantAsMemoryDb(GetServiceConfig<QdrantConfig>("Qdrant"));
                break;

            case { } x when x.Equals("Redis", StringComparison.OrdinalIgnoreCase):
                _builder.Services.AddRedisAsMemoryDb(GetServiceConfig<RedisConfig>("Redis"));
                break;

            case { } x when x.Equals("SimpleVectorDb", StringComparison.OrdinalIgnoreCase):
                _builder.Services.AddSimpleVectorDbAsMemoryDb(GetServiceConfig<SimpleVectorDbConfig>("SimpleVectorDb"));
                break;

            case { } x when x.Equals("SimpleTextDb", StringComparison.OrdinalIgnoreCase):
                _builder.Services.AddSimpleTextDbAsMemoryDb(GetServiceConfig<SimpleTextDbConfig>("SimpleTextDb"));
                break;

            case { } x when x.Equals("SqlServer", StringComparison.OrdinalIgnoreCase):
                _builder.Services.AddSqlServerAsMemoryDb(GetServiceConfig<SqlServerConfig>("SqlServer"));
                break;

        }
    }


    private void ConfigureTextGenerator()
    {
        // Text generation
        switch (_memoryConfiguration.TextGeneratorType)
        {
            case { } x when x.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase):
            case { } y when y.Equals("AzureOpenAIText", StringComparison.OrdinalIgnoreCase):
                _builder.Services.AddAzureOpenAITextGeneration(
                    GetServiceConfig<AzureOpenAIConfig>("AzureOpenAIText"));
                break;

            case { } x when x.Equals("OpenAI", StringComparison.OrdinalIgnoreCase):
                _builder.Services.AddOpenAITextGeneration(
                    GetServiceConfig<OpenAIConfig>("OpenAI"));
                break;

            case { } x when x.Equals("Anthropic", StringComparison.OrdinalIgnoreCase):
                _builder.Services.AddAnthropicTextGeneration(
                    GetServiceConfig<AnthropicConfig>("Anthropic"));
                break;

            case { } x when x.Equals("Ollama", StringComparison.OrdinalIgnoreCase):
                _builder.Services.AddOllamaTextGeneration(
                    GetServiceConfig<OllamaConfig>("Ollama"));
                break;

            case { } x when x.Equals("LlamaSharp", StringComparison.OrdinalIgnoreCase):
                _builder.Services.AddLlamaSharpTextGeneration(
                    GetServiceConfig<LlamaSharpConfig>("LlamaSharp").TextModel);
                break;

        }
    }


    private void ConfigureImageOCR()
    {
        // Image OCR
        switch (_memoryConfiguration.DataIngestion.ImageOcrType)
        {
            case { } y when string.IsNullOrWhiteSpace(y):
            case { } x when x.Equals("None", StringComparison.OrdinalIgnoreCase):
                break;

            case { } x when x.Equals("AzureAIDocIntel", StringComparison.OrdinalIgnoreCase):
                _builder.Services.AddAzureAIDocIntel(GetServiceConfig<AzureAIDocIntelConfig>("AzureAIDocIntel"));
                break;

        }
    }


    /// <summary>
    /// Check the configuration for minimum requirements
    /// </summary>
    /// <param name="exitOnError">Whether to stop or return false when the config is incomplete</param>
    /// <returns>Whether the configuration is valid</returns>
    private bool MinimumConfigurationIsAvailable(bool exitOnError)
    {
        var env = Environment.GetEnvironmentVariable(AspnetEnvVar);

        if (string.IsNullOrEmpty(env)) { env = "-UNDEFINED-"; }

        string help = $"""
                       How to configure the service:

                       1. Set the ASPNETCORE_ENVIRONMENT env var to "Development" or "Production".

                          Current value: {env}

                       2. Manual configuration:

                            * Create a configuration file, either "appsettings.Development.json" or
                              "appsettings.Production.json", depending on the value of ASPNETCORE_ENVIRONMENT.

                            * Copy and customize the default settings from appsettings.json.
                              You don't need to copy everything, only the settings you want to change.

                         Automatic configuration:

                            * You can run `dotnet run setup` to launch a wizard that will guide through
                              the creation of a custom "appsettings.Development.json".

                         Adding components:

                            * If you would like to setup the service to use custom dependencies, such as a
                              custom storage or a custom LLM, you should edit Program.cs accordingly, setting
                              up your dependencies with the usual .NET dependency injection approach.

                       """;

        // Check if text generation settings
        if (string.IsNullOrEmpty(_memoryConfiguration.TextGeneratorType))
        {
            if (!exitOnError) { return false; }

            Console.WriteLine("\n******\nText generation (TextGeneratorType) is not configured.\n" + $"Please configure the service and retry.\n\n{help}\n******\n");
            Environment.Exit(-1);
        }

        // Check embedding generation ingestion settings
        if (_memoryConfiguration.DataIngestion.EmbeddingGenerationEnabled)
        {
            if (_memoryConfiguration.DataIngestion.EmbeddingGeneratorTypes.Count == 0)
            {
                if (!exitOnError) { return false; }

                Console.WriteLine("\n******\nData ingestion embedding generation (DataIngestion.EmbeddingGeneratorTypes) is not configured.\n" + $"Please configure the service and retry.\n\n{help}\n******\n");
                Environment.Exit(-1);
            }
        }

        // Check embedding generation retrieval settings
        if (string.IsNullOrEmpty(_memoryConfiguration.Retrieval.EmbeddingGeneratorType))
        {
            if (!exitOnError) { return false; }

            Console.WriteLine("\n******\nRetrieval embedding generation (Retrieval.EmbeddingGeneratorType) is not configured.\n" + $"Please configure the service and retry.\n\n{help}\n******\n");
            Environment.Exit(-1);
        }

        return true;
    }


    /// <summary>
    /// Rewrite configuration using OpenAI, if possible.
    /// </summary>
    private void SetupForOpenAI()
    {
        string openAIKey = Environment.GetEnvironmentVariable(OpenAIEnvVar)?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(openAIKey))
        {
            return;
        }

        var inMemoryConfig = new Dictionary<string, string?>
        {
            { $"{ConfigRoot}:Services:OpenAI:APIKey", openAIKey },
            { $"{ConfigRoot}:TextGeneratorType", "OpenAI" },
            { $"{ConfigRoot}:DataIngestion:EmbeddingGeneratorTypes:0", "OpenAI" },
            { $"{ConfigRoot}:Retrieval:EmbeddingGeneratorType", "OpenAI" }
        };

        var newAppSettings = new ConfigurationBuilder();
        newAppSettings.AddConfiguration(_globalSettings);
        newAppSettings.AddInMemoryCollection(inMemoryConfig);

        _globalSettings = newAppSettings.Build();
        _memoryConfiguration = _globalSettings.GetSection(ConfigRoot).Get<KernelMemoryConfig>()!;
    }


    /// <summary>
    /// Get an instance of T, using dependencies available in the builder,
    /// except for existing service descriptors for T. Replace/Use the
    /// given action to define T's implementation.
    /// Return an instance of T built using the definition provided by
    /// the action.
    /// </summary>
    /// <param name="addCustomService">Action used to configure the service collection</param>
    /// <typeparam name="T">Target type/interface</typeparam>
    private T GetServiceInstance<T>(Action<IServiceCollection> addCustomService)
    {
        // Clone the list of service descriptors, skipping T descriptor
        IServiceCollection services = new ServiceCollection();

        foreach (ServiceDescriptor d in _builder.Services.Where(d => d.ServiceType != typeof(T)))
        {
            services.Add(d);
        }

        // Add the custom T descriptor
        addCustomService.Invoke(services);

        // Build and return an instance of T, as defined by `addCustomService`
        return services.BuildServiceProvider().GetService<T>()
            ?? throw new ConfigurationException($"Unable to build {nameof(T)}");
    }


    /// <summary>
    /// Read a dependency configuration from IConfiguration
    /// Data is usually retrieved from KernelMemory:Services:{serviceName}, e.g. when using appsettings.json
    /// {
    ///   "KernelMemory": {
    ///     "Services": {
    ///       "{serviceName}": {
    ///         ...
    ///         ...
    ///       }
    ///     }
    ///   }
    /// }
    /// </summary>
    /// <param name="serviceName">Name of the dependency</param>
    /// <typeparam name="T">Type of configuration to return</typeparam>
    /// <returns>Configuration instance, settings for the dependency specified</returns>
    private T GetServiceConfig<T>(string serviceName)
    {
        return _memoryConfiguration.GetServiceConfig<T>(_globalSettings, serviceName);
    }

    #endregion


}
