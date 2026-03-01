// Copyright (c) Microsoft.All rights reserved.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.KernelMemory.MemoryDb.Qdrant.Internals.Http;

internal sealed class Filter
{
    internal sealed class OrClause
    {
        [JsonPropertyName("should")]
        public List<object> Clauses { get; set; }


        internal OrClause()
        {
            Clauses = [];
        }


        internal OrClause Or(object condition)
        {
            Clauses.Add(condition);
            return this;
        }


        internal OrClause OrValue(string key, object value)
        {
            return Or(new MatchValueClause(key, value));
        }


        internal void Validate()
        {
            ArgumentNullExceptionEx.ThrowIfNull(Clauses, nameof(Clauses), "Filter clauses cannot be null");

            foreach (var x in Clauses)
            {
                switch (x)
                {
                    case AndClause ac:
                        ac.Validate();
                        break;

                    case OrClause oc:
                        oc.Validate();
                        break;

                    case MatchValueClause mvc:
                        mvc.Validate();
                        break;
                }
            }
        }
    }


    internal sealed class AndClause
    {
        [JsonPropertyName("must")]
        public List<object> Clauses { get; set; }


        internal AndClause()
        {
            Clauses = [];
        }


        internal AndClause And(object condition)
        {
            Clauses.Add(condition);
            return this;
        }


        internal AndClause AndValue(string key, object value)
        {
            return And(new MatchValueClause(key, value));
        }


        internal void Validate()
        {
            ArgumentNullExceptionEx.ThrowIfNull(Clauses, nameof(Clauses), "Filter clauses cannot be null");

            foreach (var x in Clauses)
            {
                switch (x)
                {
                    case AndClause ac:
                        ac.Validate();
                        break;

                    case OrClause oc:
                        oc.Validate();
                        break;

                    case MatchValueClause mvc:
                        mvc.Validate();
                        break;
                }
            }
        }
    }


    internal sealed class MatchValueClause
    {
        [JsonPropertyName("key")]
        public string Key { get; set; }

        [JsonPropertyName("match")]
        public MatchValue Match { get; set; }


        public MatchValueClause()
        {
            Match = new MatchValue();
            Key = string.Empty;
        }


        public MatchValueClause(string key, object value) : this()
        {
            Key = key;
            Match.Value = value;
        }


        internal void Validate()
        {
            ArgumentNullExceptionEx.ThrowIfNull(Key, nameof(Key), "Match filter key cannot be null");
            ArgumentNullExceptionEx.ThrowIfNull(Match, nameof(Match), "Match filter value cannot be null");
        }
    }


    internal sealed class MatchValue
    {
        [JsonPropertyName("value")]
        public object Value { get; set; }


        public MatchValue()
        {
            Value = string.Empty;
        }
    }
}
