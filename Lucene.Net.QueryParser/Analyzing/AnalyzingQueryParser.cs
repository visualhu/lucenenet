﻿using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Tokenattributes;
using Lucene.Net.QueryParser.Classic;
using Lucene.Net.Search;
using Lucene.Net.Util;
using System.Text;
using System.Text.RegularExpressions;

namespace Lucene.Net.QueryParser.Analyzing
{
    /*
     * Licensed to the Apache Software Foundation (ASF) under one or more
     * contributor license agreements.  See the NOTICE file distributed with
     * this work for additional information regarding copyright ownership.
     * The ASF licenses this file to You under the Apache License, Version 2.0
     * (the "License"); you may not use this file except in compliance with
     * the License.  You may obtain a copy of the License at
     *
     *     http://www.apache.org/licenses/LICENSE-2.0
     *
     * Unless required by applicable law or agreed to in writing, software
     * distributed under the License is distributed on an "AS IS" BASIS,
     * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
     * See the License for the specific language governing permissions and
     * limitations under the License.
     */

    /// <summary>
    /// Overrides Lucene's default QueryParser so that Fuzzy-, Prefix-, Range-, and WildcardQuerys
    /// are also passed through the given analyzer, but wildcard characters <code>*</code> and
    /// <code>?</code> don't get removed from the search terms.
    /// 
    /// <p><b>Warning:</b> This class should only be used with analyzers that do not use stopwords
    /// or that add tokens. Also, several stemming analyzers are inappropriate: for example, GermanAnalyzer 
    /// will turn <code>H&auml;user</code> into <code>hau</code>, but <code>H?user</code> will 
    /// become <code>h?user</code> when using this parser and thus no match would be found (i.e.
    /// using this parser will be no improvement over QueryParser in such cases). 
    /// </summary>
    public class AnalyzingQueryParser : Classic.QueryParser
    {
        // gobble escaped chars or find a wildcard character 
        private readonly Regex wildcardPattern = new Regex(@"(\\.)|([?*]+)", RegexOptions.Compiled);

        public AnalyzingQueryParser(LuceneVersion matchVersion, string field, Analyzer analyzer)
            : base(matchVersion, field, analyzer)
        {
            AnalyzeRangeTerms = true;
        }

        /// <summary>
        /// Called when parser parses an input term
        /// that uses prefix notation; that is, contains a single '*' wildcard
        /// character as its last character. Since this is a special case
        /// of generic wildcard term, and such a query can be optimized easily,
        /// this usually results in a different query object.
        /// <p>
        /// Depending on analyzer and settings, a prefix term may (most probably will)
        /// be lower-cased automatically. It <b>will</b> go through the default Analyzer.
        /// <p>
        /// Overrides super class, by passing terms through analyzer.
        /// </summary>
        /// <param name="field">Name of the field query will use.</param>
        /// <param name="termStr">Term to use for building term for the query
        /// (<b>without</b> trailing '*' character!)</param>
        /// <returns>Resulting <see cref="Query"/> built for the term</returns>
        protected internal override Query GetWildcardQuery(string field, string termStr)
        {
            if (termStr == null)
            {
                //can't imagine this would ever happen
                throw new ParseException("Passed null value as term to getWildcardQuery");
            }
            if (!AllowLeadingWildcard && (termStr.StartsWith("*") || termStr.StartsWith("?")))
            {
                throw new ParseException("'*' or '?' not allowed as first character in WildcardQuery"
                                        + " unless getAllowLeadingWildcard() returns true");
            }

            Match wildcardMatcher = wildcardPattern.Match(termStr);
            StringBuilder sb = new StringBuilder();
            int last = 0;

            while (wildcardMatcher.Success)
            {
                // continue if escaped char
                if (wildcardMatcher.Groups[1].Success)
                {
                    wildcardMatcher = wildcardMatcher.NextMatch();
                    continue;
                }

                if (wildcardMatcher.Index > last)
                {
                    string chunk = termStr.Substring(last, wildcardMatcher.Index - last);
                    string analyzed = AnalyzeSingleChunk(field, termStr, chunk);
                    sb.Append(analyzed);
                }

                //append the wildcard character
                sb.Append(wildcardMatcher.Groups[2]);

                last = wildcardMatcher.Index + wildcardMatcher.Length;
                wildcardMatcher = wildcardMatcher.NextMatch();
            }
            if (last < termStr.Length)
            {
                sb.Append(AnalyzeSingleChunk(field, termStr, termStr.Substring(last)));
            }
            return base.GetWildcardQuery(field, sb.ToString());
        }

        /// <summary>
        /// Called when parser parses an input term that has the fuzzy suffix (~) appended.
        /// <p>
        /// Depending on analyzer and settings, a fuzzy term may (most probably will)
        /// be lower-cased automatically. It <b>will</b> go through the default Analyzer.
        /// <p>
        /// Overrides super class, by passing terms through analyzer.
        /// </summary>
        /// <param name="field">Name of the field query will use.</param>
        /// <param name="termStr">Term to use for building term for the query</param>
        /// <param name="minSimilarity"></param>
        /// <returns>Resulting <see cref="Query"/> built for the term</returns>
        protected internal override Query GetFuzzyQuery(string field, string termStr, float minSimilarity)
        {
            string analyzed = AnalyzeSingleChunk(field, termStr, termStr);
            return base.GetFuzzyQuery(field, analyzed, minSimilarity);
        }

        /// <summary>
        /// Returns the analyzed form for the given chunk
        /// 
        /// If the analyzer produces more than one output token from the given chunk,
        /// a ParseException is thrown.
        /// </summary>
        /// <param name="field">The target field</param>
        /// <param name="termStr">The full term from which the given chunk is excerpted</param>
        /// <param name="chunk">The portion of the given termStr to be analyzed</param>
        /// <returns>The result of analyzing the given chunk</returns>
        /// <exception cref="ParseException">ParseException when analysis returns other than one output token</exception>
        protected internal string AnalyzeSingleChunk(string field, string termStr, string chunk)
        {
            string analyzed = null;
            TokenStream stream = null;
            try
            {
                stream = Analyzer.TokenStream(field, chunk);
                stream.Reset();
                ICharTermAttribute termAtt = stream.GetAttribute<ICharTermAttribute>();
                // get first and hopefully only output token
                if (stream.IncrementToken())
                {
                    analyzed = termAtt.ToString();

                    // try to increment again, there should only be one output token
                    StringBuilder multipleOutputs = null;
                    while (stream.IncrementToken())
                    {
                        if (null == multipleOutputs)
                        {
                            multipleOutputs = new StringBuilder();
                            multipleOutputs.Append('"');
                            multipleOutputs.Append(analyzed);
                            multipleOutputs.Append('"');
                        }
                        multipleOutputs.Append(',');
                        multipleOutputs.Append('"');
                        multipleOutputs.Append(termAtt.ToString());
                        multipleOutputs.Append('"');
                    }
                    stream.End();
                    if (null != multipleOutputs)
                    {
                        throw new ParseException(
                            string.Format(Locale, "Analyzer created multiple terms for \"%s\": %s", chunk, multipleOutputs.ToString()));
                    }
                }
                else
                {
                    // nothing returned by analyzer.  Was it a stop word and the user accidentally
                    // used an analyzer with stop words?
                    stream.End();
                    throw new ParseException(string.Format(Locale, "Analyzer returned nothing for \"%s\"", chunk));
                }
            }
            catch (System.IO.IOException e)
            {
                throw new ParseException(
                    string.Format(Locale, "IO error while trying to analyze single term: \"%s\"", termStr));
            }
            finally
            {
                IOUtils.CloseWhileHandlingException(stream);
            }
            return analyzed;
        }
    }
}
