// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.ML.Tokenizers
{
    /// <summary>
    /// Represent the Byte Pair Encoding model.
    /// </summary>
    public sealed class MyBpe : Model
    {
        /// A [Byte Pair Encoding](https://www.aclweb.org/anthology/P16-1162/) model.

        private string? _unknownToken;

        /// <summary>
        /// Gets or Sets unknown token. The unknown token to be used when we encounter an unknown char
        /// </summary>
        public string? UnknownToken
        {
            get
            {
                return _unknownToken;
            }

            set
            {
                _unknownToken = value;

                if( value is null )
                {
                    if( VocabReverse.TryGetValue( 0, out string v ) )
                    {
                        VocabReverse.Remove( 0 );
                        if( Vocab.TryGetValue( v, out int id ) )
                        {
                            Vocab.Remove( v );
                        }
                    }
                }
                else
                {
                    Vocab[value] = 0;
                    VocabReverse[0] = value;
                }
            }
        }

        /// <summary>
        /// An optional prefix to use on any sub-word that exist only behind another one
        /// </summary>
        public string? ContinuingSubwordPrefix { get; set; }

        /// <summary>
        /// An optional suffix to characterize and end-of-word sub-word
        /// </summary>
        public string? EndOfWordSuffix { get; set; }

        /// <summary>
        /// Gets or sets whether allowing multiple unknown tokens get fused
        /// </summary>
        public bool FuseUnknownTokens { get; set; }

        /// <summary>
        /// Construct a new Bpe model object with no tokenization vocabulary. This constructor is useful only in the training scenario.
        /// </summary>
        public MyBpe()
        {
            Vocab = new();
            VocabReverse = new();
            Merges = new();

            UnknownToken = "[Unk]";
        }

        /// <summary>
        /// Construct a new Bpe model object to use for sentence tokenization and tokenizer training.
        /// </summary>
        /// <param name="vocabFile">The JSON file path containing the dictionary of string keys and their ids.</param>
        /// <param name="mergesFile">The file path containing the tokens's pairs list.</param>
        /// <param name="unknownToken"> The unknown token to be used by the model.</param>
        /// <param name="continuingSubwordPrefix">The prefix to attach to sub-word units that don’t represent a beginning of MyWord.</param>
        /// <param name="endOfWordSuffix">The suffix to attach to sub-word units that represent an end of MyWord.</param>
        public MyBpe( string vocabFile, string? mergesFile, string? unknownToken = null, string? continuingSubwordPrefix = null, string? endOfWordSuffix = null )
        {
            ContinuingSubwordPrefix = continuingSubwordPrefix;
            EndOfWordSuffix = endOfWordSuffix;

            (Dictionary<string, int>? vocab1, MyVec<(string, string)> merges) = ReadFile( vocabFile, mergesFile );
            Vocab = vocab1 ?? new Dictionary<string, int>();

            VocabReverse = new();

            foreach( KeyValuePair<string, int> kvp in Vocab )
            {
                VocabReverse.Add( kvp.Value, kvp.Key );
            }

            if( unknownToken is null && VocabReverse.TryGetValue( 0, out string unkToken ) )
            {
                unknownToken = unkToken;
            }

            UnknownToken = unknownToken;

            int prefixLen = ContinuingSubwordPrefix is null ? 0 : ContinuingSubwordPrefix.Length;

            Merges = new();
            for( int i = 0; i < merges.Count; i++ )
            {
                (string a, string b) mergeValues = merges[i];

                if( !Vocab.TryGetValue( mergeValues.a, out int aId ) )
                {
                    throw new InvalidOperationException( $"Trying to merge a token {mergeValues.a} which not exist in the vocabulary." );
                }

                if( !Vocab.TryGetValue( mergeValues.b, out int bId ) )
                {
                    throw new InvalidOperationException( $"Trying to merge a token {mergeValues.b} which not exist in the vocabulary." );
                }
                var firstToken = mergeValues.a.StartsWith( continuingSubwordPrefix ) ?
                    mergeValues.a.Substring( prefixLen ) : mergeValues.a;
                string newToken = $"{firstToken}{mergeValues.b}";
                if( !Vocab.TryGetValue( newToken, out int newId ) )
                {
                    newToken = $"{mergeValues.a}{mergeValues.b}";
                    if( !Vocab.TryGetValue( newToken, out newId ) )
                    {
                        throw new InvalidOperationException( $"Trying to merge a token {newToken} which not exist in the vocabulary." );
                    }
                }

                Merges.Add( new Pair<int>( aId, bId ), (i, newId) );
            }
        }

        /// <summary>
        /// Gets the Bpe decoder object.
        /// </summary>
        public static TokenizerDecoder Decoder { get; } = new BpeDecoder();

        /// <summary>
        /// Tokenize a sequence string to a list of tokens.
        /// </summary>
        /// <param name="sequence">The sequence to tokenize.</param>
        /// <returns>The list of tokens generated from the sequence tokenization.</returns>
        public override IReadOnlyList<Token> Tokenize( string sequence )
        {
            if( sequence.Length == 0 )
            {
                return EmptyTokensList;
            }

            if( !Dropout.HasValue )
            {
                return TokenizeWithCache( sequence );
            }

            MyWord MyWord = MergeWord( sequence );

            return MyWordToTokens( ref MyWord );
        }

        /// <summary>
        /// Map the token to tokenized Id.
        /// </summary>
        /// <param name="token">The token to map to the Id.</param>
        /// <returns>The mapped Id of the token.</returns>
        public override int? TokenToId( string token )
        {
            if( Vocab.TryGetValue( token, out int value ) )
            {
                return value;
            }

            return null;
        }

        /// <summary>
        /// Map the tokenized Id to the token.
        /// </summary>
        /// <param name="id">The Id to map to the token.</param>
        /// <param name="skipSpecialTokens">Indicate if want to skip the special tokens during the decoding.</param>
        /// <returns>The mapped token of the Id.</returns>
        public override string? IdToToken( int id, bool skipSpecialTokens = false )
        {
            if( VocabReverse.TryGetValue( id, out string value ) )
            {
                return value;
            }

            return null;
        }

        /// <summary>
        /// Gets the dictionary mapping tokens to Ids.
        /// </summary>
        public override IReadOnlyDictionary<string, int> GetVocab() => Vocab;

        /// <summary>
        /// Gets the dictionary size that map tokens to Ids.
        /// </summary>
        public override int GetVocabSize() => Vocab.Count;

        /// <summary>
        /// Gets a trainer object to use in training the model and generate the vocabulary and merges data.
        /// </summary>
        public override Trainer? GetTrainer() => new BpeTrainer();

        /// <summary>
        /// Save the model data into the vocabulary and merges files.
        /// </summary>
        /// <param name="path">The file system path to store the generated files at.</param>
        /// <param name="prefix">Optional prefix for the generated file names.</param>
        /// <returns>The list of all saved files.</returns>
        public override string[] Save( string path, string? prefix = null )
        {
            // Write vocab.json
            string vocabFileNname = prefix is null ? "vocab.json" : $"{prefix}-vocab.json";
            string vocabPath = Path.Combine( path, vocabFileNname );
            string serialized = JsonSerializer.Serialize( VocabReverse, new JsonSerializerOptions { Converters = { new DictReversingConverter() } } );
            File.WriteAllText( vocabPath, serialized, System.Text.Encoding.UTF8 );

            // Write merges.txt
            string mergeFileName = prefix is null ? "merges.txt" : $"{prefix}-merges.txt";
            string mergePath = Path.Combine( path, mergeFileName );
            (Pair<int> pair, int rank)[] pairsArray = new (Pair<int>, int)[Merges.Count];
            int i = 0;
            foreach( var p in Merges )
            {
                pairsArray[i++] = (p.Key, p.Value.Item1 /* rank */);
            }
            Array.Sort( pairsArray, ( x, y ) => x.rank.CompareTo( y.rank ) );
            using StreamWriter file = new( mergePath, append: false, System.Text.Encoding.UTF8 );
            file.WriteLine( "#version: 0.2 - Trained by `huggingface/tokenizers`" );
            foreach( var p in pairsArray )
            {
                file.WriteLine( $"{VocabReverse[p.pair.First]} {VocabReverse[p.pair.Second]}" );
            }

            return new string[] { vocabPath, mergePath };
        }

        /// Read the given files to extract the vocab and merges
        internal static (Dictionary<string, int>?, MyVec<(string, string)>) ReadFile( string? vocab, string? merges )
        {
            Dictionary<string, int>? dic;
            using( Stream stream = File.OpenRead( vocab ) )
            {
                dic = JsonSerializer.Deserialize<Dictionary<string, int>>( stream ) as Dictionary<string, int>;
            }

            return (dic, ConvertMergesToHashmap( merges ));
        }

        /// The vocabulary assigns a number to each token.
        internal Dictionary<string, int> Vocab { get; set; }

        /// Contains the mapping between Pairs and their (rank, newId).
        internal Dictionary<Pair<int>, (int, int)> Merges { get; set; }

        /// Contains the cache for optimizing the encoding step.
        internal Cache<string, MyWord>? Cache { get; set; }

        internal static readonly int DefaultCacheCapacity = 10_000;

        /// Reversed vocabulary, to rebuild sentences.
        internal SortedDictionary<int, string> VocabReverse { get; set; }

        /// Dropout probability for merges. 0 = no dropout is the default. At 1.0, tokenization will
        /// perform no merges, so the result will just be characters.
        internal float? Dropout { get; set; }

        /// Converts the merges strings (for example from `merges.txt` file) with the format
        /// "{pair_a} {pair_b}" into the format expected by the BPE struct
        internal static MyVec<(string, string)> ConvertMergesToHashmap( string? mergesFile )
        {
            if( mergesFile is null )
            {
                return new MyVec<(string, string)>();
            }

            MyVec<(string, string)> merges = new( 1000 );

            int lineNumber = 0;
            foreach( string line in System.IO.File.ReadLines( mergesFile ) )
            {
                lineNumber++;
                if( line.StartsWith( "#version", StringComparison.Ordinal ) || line.Length == 0 )
                {
                    continue;
                }
                int index = line.IndexOf( ' ' );
                if( index < 0 || index == line.Length - 1 || line.IndexOf( ' ', index + 1 ) >= 0 )
                {
                    throw new InvalidOperationException( $"Invalid merger file format at line: {lineNumber}" );
                }
                merges.Push( (line.Substring( 0, index ), line.Substring( index + 1 )) );
            }

            return merges;
        }

        /// Reset the cache.
        internal void ClearCache() => Cache?.Clear();

        private readonly Dictionary<char, string> _charToString = new Dictionary<char, string>();

        [MethodImpl( MethodImplOptions.AggressiveInlining )]
        internal string CharToString( char c )
        {
            if( _charToString.TryGetValue( c, out string v ) )
            {
                return v;
            }

            string s = c.ToString();
            _charToString[c] = s;
            return s;
        }

        internal MyWord MergeWord( string w )
        {
            MyWord MyWord = MyWord.WithCapacity( (int)w.Length );
            (int Id, int Len)? unk = null;
            int i = 0;

            while( i < w.Length )
            {
                int length;
                string s;

                if( Char.IsHighSurrogate( w[i] ) && i < w.Length - 1 && Char.IsLowSurrogate( w[i + 1] ) )
                {
                    length = 2;
                    s = w.Substring( i, (int)length );
                }
                else
                {
                    length = 1;
                    s = CharToString( w[i] );
                }

                // Add the `continuing_subword_prefix` if relevant
                if( i > 0 && ContinuingSubwordPrefix is not null )
                {
                    s = $"{ContinuingSubwordPrefix}{s}";
                }

                // Add the `end_of_word_suffix` if relevant
                if( i + length >= w.Length && EndOfWordSuffix is not null )
                {
                    s = $"{s}{EndOfWordSuffix}";
                }

                if( Vocab.TryGetValue( s, out int id ) )
                {
                    if( unk.HasValue )
                    {
                        MyWord.Add( unk.Value.Id, unk.Value.Len );
                        unk = null;
                    }
                    MyWord.Add( id, length );
                }
                else if( UnknownToken is not null )
                {
                    if( unk.HasValue )
                    {
                        if( FuseUnknownTokens )
                        {
                            // Fuse unk
                            unk = (unk.Value.Id, unk.Value.Len + length);
                        }
                        else
                        {
                            // Do not fuse unk, add the previous one
                            MyWord.Add( unk.Value.Id, unk.Value.Len );
                            if( !Vocab.TryGetValue( UnknownToken, out int value ) )
                            {
                                throw new InvalidOperationException( $"Unknown Token Out Of Vocabulary." );
                            }
                            unk = (value, length);
                        }
                    }
                    else
                    {
                        if( !Vocab.TryGetValue( UnknownToken, out int value ) )
                        {
                            throw new InvalidOperationException( $"Unknown Token Out Of Vocabulary." );
                        }
                        unk = (value, length);
                    }
                }

                i += (int)length;
            }

            if( unk.HasValue )
            {
                MyWord.Add( unk.Value.Id, unk.Value.Len );
            }

            MyWord.MergeAll( Merges, Dropout );
            return MyWord;
        }

        // internal MyWord.Enumerator MyWordToTokens(Word MyWord) => MyWord.GetIterator(VocabReverse);
        internal List<Token> MyWordToTokens( ref MyWord MyWord )
        {
            List<Token> tokens = new( MyWord.SymbolsCount );

            foreach( Token token in MyWord.GetIterator( VocabReverse ) )
            {
                tokens.Add( token );
            }

            return tokens;
        }

        internal List<Token> TokenizeWithCache( string sequence )
        {
            if( Cache is not null )
            {
                MyWord? hit = Cache.Get( sequence );
                if( hit.HasValue )
                {
                    MyWord w = hit.Value;
                    return MyWordToTokens( ref w );
                }
            }

            MyWord MyWord = MergeWord( sequence );
            List<Token> tokens = MyWordToTokens( ref MyWord );

            if( Cache is not null )
            {
                Cache.Set( sequence, MyWord );
            }

            return tokens;
        }

        internal static readonly List<Token> EmptyTokensList = new();
    }

    internal struct MyVec<T>
    {
        private const int DefaultCapacity = 10;
        private int _count;
        private T[]? _buffer;

        public ref T this[int index]
        {
            get
            {
                if( index >= _count )
                {
                    throw new ArgumentOutOfRangeException( nameof( index ), $"{index} is out of range" );
                }
                return ref _buffer![index];
            }
        }

        public MyVec()
        {
            _count = 0;
            _buffer = null;
        }

        public MyVec( int capacity )
        {
            _count = 0;
            _buffer = new T[capacity];
        }

        public int Capacity => _buffer is null ? 0 : _buffer.Length;
        public int Count => _count;

        public void Push( T t )
        {
            if( _buffer is null )
            {
                _buffer = new T[DefaultCapacity];
                _buffer[0] = t;
                _count = 1;
                return;
            }

            if( _buffer.Length <= _count )
            {
                Array.Resize( ref _buffer, _buffer.Length << 1 );
            }

            _buffer[_count++] = t;
        }

        public void Remove( int index )
        {
            if( index >= _count || _buffer is null )
            {
                return;
            }

            for( int i = index; i < _count - 1; i++ )
            {
                _buffer[i] = _buffer[i + 1];
            }

            _count--;
        }

        public void Clear() => _count = 0;
    }

    struct Pair<T> : IEquatable<Pair<T>>, IComparable<Pair<T>> where T : struct, IEquatable<T>, IComparable<T>
    {
        public T First { get; set; }
        public T Second { get; set; }

        public static Pair<T> Create( T first, T second ) => new Pair<T>( first, second );

        public Pair( T first, T second )
        {
            First = first;
            Second = second;
        }

        public bool Equals( Pair<T> other ) => First.Equals( other.First ) && Second.Equals( other.Second );

        public override int GetHashCode()
        {
            int hashcode = 23;
            hashcode = (hashcode * 37) + First.GetHashCode();
            hashcode = (hashcode * 37) + Second.GetHashCode();
            return hashcode;

        }

        public int CompareTo( Pair<T> other )
        {
            int compareFirst = First.CompareTo( other.First );
            return compareFirst == 0 ? Second.CompareTo( other.Second ) : compareFirst;
        }
    }

    internal struct MyWord
    {
        private static readonly Random _random = new Random();
        private MyVec<MySymbol> _symbols;

        public MyWord() => _symbols = new MyVec<MySymbol>();

        public MyWord( int capacity )
        {
            if( capacity > int.MaxValue )
            {
                throw new ArgumentOutOfRangeException( nameof( capacity ) );
            }
            _symbols = new MyVec<MySymbol>( (int)capacity );
        }

        public static MyWord WithCapacity( int capacity ) => new MyWord( capacity );

        public int SymbolsCount => _symbols.Count;

        public void Add( int c, int charLength )
        {
            int prev = -1;
            int next = -1;

            int len = _symbols.Count;

            if( len > 0 )
            {
                // Update `next` on the previous one
                _symbols[len - 1].Next = len;
                prev = len - 1;
            }

            _symbols.Push( new MySymbol( c, prev, next, charLength ) );
        }

        public MyVec<(Pair<int>, int)> Merge( int c1, int c2, int replacement )
        {
            MyVec<(Pair<int>, int)> changes = new();
            int i = 0;

            while( true )
            {
                if( i >= _symbols.Count )
                {
                    break;
                }

                // Found a pair
                if( _symbols[i].C == c1 && i + 1 < _symbols.Count && _symbols[i + 1].C == c2 )
                {
                    MySymbol first = _symbols[i];
                    MySymbol second = _symbols[i + 1];

                    // If there are other characters before the pair
                    if( i > 0 )
                    {
                        changes.Push( (Pair<int>.Create( _symbols[i - 1].C, first.C ), -1) );
                        changes.Push( (Pair<int>.Create( _symbols[i - 1].C, replacement ), 1) );
                    }

                    // Remove in place
                    // Insert replacement before first char of pair
                    // Remove first char of pair
                    // And then the second

                    _symbols[i].C = replacement;
                    _symbols[i].Prev = first.Prev;
                    _symbols[i].Next = second.Next;
                    _symbols[i].Len = first.Len + second.Len;

                    _symbols.Remove( i + 1 );

                    // If there are other characters after the pair
                    if( i < _symbols.Count - 1 )
                    {
                        changes.Push( (Pair<int>.Create( second.C, _symbols[i + 1].C ), -1) );
                        changes.Push( (Pair<int>.Create( replacement, _symbols[i + 1].C ), 1) );
                    }
                }

                i += 1;
            };

            return changes;
        }

        public void MergeAll( Dictionary<Pair<int>, (int, int)> merges, float? dropout )
        {
            // Queue<Merge> queue = new Queue<Merge>(_symbols.Count);
            PriorityQueue<MyMerge> queue = new PriorityQueue<MyMerge>( _symbols.Count );

            MyVec<MyMerge> skip = new MyVec<MyMerge>( queue.Count );

            for( int i = 0; i < _symbols.Count - 1; i++ )
            {
                if( merges.TryGetValue( Pair<int>.Create( _symbols[i].C, _symbols[i + 1].C ), out (int m1, int m2) value ) )
                {
                    queue.Enqueue( new MyMerge( i, value.m1, value.m2 ) );
                }
            }

            while( queue.Count > 0 )
            {
                MyMerge top = queue.Dequeue();
                if( dropout.HasValue && _random.NextDouble() < dropout )
                {
                    skip.Push( top );
                }
                else
                {
                    // Re-insert the skipped elements
                    for( int i = 0; i < skip.Count; i++ )
                    {
                        queue.Enqueue( skip[i] );
                    }
                    skip.Clear();

                    // Do nothing if we are the last MySymbol
                    if( _symbols.Count == 0 || _symbols[top.Pos].Len == 0 || _symbols[top.Pos].Next == -1 )
                    {
                        continue;
                    }

                    int nextPos = _symbols[top.Pos].Next;
                    MySymbol right = _symbols[nextPos];

                    // Make sure we are not processing an expired queue entry
                    Pair<int> targetNewPair = Pair<int>.Create( _symbols[top.Pos].C, right.C );
                    if( !merges.TryGetValue( targetNewPair, out (int m1, int m2) value ) || value.m2 != top.NewId )
                    {
                        continue;
                    }

                    // Otherwise, let's merge
                    _symbols[top.Pos].MergeWith( ref right, top.NewId );

                    // Tag the right part as removed
                    _symbols[nextPos].Len = 0;

                    // Update `prev` on the new `next` to the current pos
                    if( right.Next > -1 && right.Next < _symbols.Count )
                    {
                        _symbols[right.Next].Prev = top.Pos;
                    }

                    // Insert the new pair formed with the previous MySymbol
                    MySymbol current = _symbols[top.Pos];
                    if( current.Prev >= 0 )
                    {
                        int prev = current.Prev;
                        MySymbol prevSymbol = _symbols[prev];
                        Pair<int> newPair = Pair<int>.Create( prevSymbol.C, current.C );

                        if( merges.TryGetValue( newPair, out value ) )
                        {
                            queue.Enqueue( new MyMerge( current.Prev, value.m1, value.m2 ) );
                        }
                    }

                    // Insert the new pair formed with the next MySymbol
                    int next = current.Next;
                    if( (uint)next < (uint)_symbols.Count )
                    {
                        MySymbol nextSymbol = _symbols[(int)next];
                        Pair<int> newPair = Pair<int>.Create( current.C, nextSymbol.C );
                        if( merges.TryGetValue( newPair, out value ) )
                        {
                            queue.Enqueue( new MyMerge( top.Pos, value.m1, value.m2 ) );
                        }
                    }
                }
            }

            // Filter out the removed MySymbols
            for( int i = _symbols.Count - 1; i >= 0; i-- )
            {
                if( _symbols[i].Len == 0 )
                {
                    _symbols.Remove( i );
                }
            }
        }

        public MyVec<int> GetChars()
        {
            MyVec<int> chars = new MyVec<int>();
            for( int i = 0; i < _symbols.Count; i++ )
            {
                chars.Push( _symbols[i].C );
            }

            return chars;
        }

        public override string ToString()
        {
            if( _symbols.Count == 0 )
            {
                return "[]";
            }

            StringBuilder sb = new StringBuilder();
            sb.Append( '[' );
            sb.Append( $"{_symbols[0].C}" );
            for( int i = 1; i < _symbols.Count; i++ )
            {
                sb.Append( $", {_symbols[i].C}" );
            }
            sb.Append( ']' );
            return sb.ToString();
        }

        public Enumerator GetIterator( SortedDictionary<int, string> vocabReverse ) => new Enumerator( ref _symbols, vocabReverse );

        public struct Enumerator
        {
            private int _index;
            private int _pos;
            private MyVec<MySymbol> _symbols;
            private readonly SortedDictionary<int, string> _vocabReverse;

            public Enumerator( ref MyVec<MySymbol> MySymbols, SortedDictionary<int, string> vocabReverse )
            {
                _index = -1;
                _pos = 0;
                _symbols = MySymbols;
                _vocabReverse = vocabReverse;
            }

            public readonly Enumerator GetEnumerator() => this;

            public readonly Token Current => new Token( _symbols[_index].C, _vocabReverse[_symbols[_index].C], (_pos, _pos + _symbols[_index].Len) );

            public bool MoveNext()
            {
                if( _symbols.Count == 0 || _index >= _symbols.Count - 1 )
                {
                    return false;
                }

                _pos = _index == -1 ? 0 : _pos + _symbols[_index].Len;

                _index++;
                return true;
            }
        }
    }

    internal struct MySymbol
    {
        internal int C { get; set; }
        internal int Prev { get; set; }
        internal int Next { get; set; }
        internal int Len { get; set; } // number of characters

        public MySymbol( int c, int prev, int next, int len )
        {
            C = c;
            Prev = prev;
            Next = next;
            Len = len;
        }

        /// Merges the current MySymbol with the other one.
        /// In order to update prev/next, we consider Self to be the MySymbol on the left,
        /// and other to be the next one on the right.
        internal void MergeWith( ref MySymbol other, int c )
        {
            C = c;
            Len += other.Len;
            Next = other.Next;
        }
    }

    class DictReversingConverter : JsonConverter<SortedDictionary<int, string>>
    {
        public override SortedDictionary<int, string>? Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options ) => null;

        public override void Write( Utf8JsonWriter writer, SortedDictionary<int, string> value, JsonSerializerOptions options )
        {
            writer.WriteStartObject();

            foreach( KeyValuePair<int, string> pair in value )
            {
                if( pair.Key >= 0 )
                {
                    writer.WriteNumber( pair.Value, pair.Key );
                }
            }

            writer.WriteEndObject();
        }
    }

    internal sealed class Cache<TKey, TValue>
    {
        internal Cache() : this( MyBpe.DefaultCacheCapacity ) { }

        internal Cache( int capacity )
        {
            Capacity = capacity;
            Map = new Dictionary<TKey, TValue>( (int)Capacity );
        }

        private readonly ReaderWriterLockSlim _cacheLock = new ReaderWriterLockSlim();

        internal Dictionary<TKey, TValue> Map { get; set; }

        internal int Capacity { get; set; }

        internal void Fresh() => Map = new Dictionary<TKey, TValue>( (int)Capacity );

        internal void Clear()
        {
            _cacheLock.EnterWriteLock();
            try
            {
                Map.Clear();
            }
            finally { _cacheLock.ExitWriteLock(); }
        }

        internal List<TValue> GetValues( IEnumerable<TKey> keys )
        {
            List<TValue>? values = new();
            _cacheLock.EnterReadLock();
            try
            {
                foreach( TKey key in keys )
                {
                    if( Map.TryGetValue( key, out TValue value ) )
                    {
                        values.Add( value );
                    }
                }
            }
            finally { _cacheLock.ExitReadLock(); }

            return values;
        }

        internal TValue? Get( TKey key )
        {
            _cacheLock.EnterReadLock();
            try
            {
                if( Map.TryGetValue( key, out TValue value ) )
                {
                    return value;
                }
            }
            finally { _cacheLock.ExitReadLock(); }

            return default;
        }

        internal void SetValues( IEnumerable<(TKey, TValue)> enteries )
        {
            _cacheLock.EnterWriteLock();
            try
            {
                foreach( (TKey, TValue) entry in enteries )
                {
                    if( Capacity <= Map.Count )
                    {
                        break;
                    }
                    Map[entry.Item1] = entry.Item2;
                }
            }
            finally { _cacheLock.ExitWriteLock(); }
        }

        internal void Set( TKey k, TValue v )
        {
            _cacheLock.EnterWriteLock();
            try
            {
                if( Capacity > Map.Count )
                {
                    Map[k] = v;
                }
            }
            finally { _cacheLock.ExitWriteLock(); }
        }
    }


    internal class PriorityQueue<T> where T : IComparable<T>
    {
        private readonly List<T> _data;

        public PriorityQueue( int capacity )
        {
            _data = new List<T>( capacity );
        }

        public void Enqueue( T item )
        {
            _data.Add( item );
            int ci = _data.Count - 1; // child index; start at end
            while( ci > 0 )
            {
                int pi = (ci - 1) / 2; // parent index
                if( _data[ci].CompareTo( _data[pi] ) >= 0 ) break; // child item is larger than (or equal) parent so we're done
                T tmp = _data[ci]; _data[ci] = _data[pi]; _data[pi] = tmp;
                ci = pi;
            }
        }

        public T Dequeue()
        {
            // assumes pq is not empty; up to calling code
            int li = _data.Count - 1; // last index (before removal)
            T frontItem = _data[0];   // fetch the front
            _data[0] = _data[li];
            _data.RemoveAt( li );

            --li; // last index (after removal)
            int pi = 0; // parent index. start at front of pq
            while( true )
            {
                int ci = pi * 2 + 1; // left child index of parent
                if( ci > li ) break;  // no children so done
                int rc = ci + 1;     // right child
                if( rc <= li && _data[rc].CompareTo( _data[ci] ) < 0 ) // if there is a rc (ci + 1), and it is smaller than left child, use the rc instead
                    ci = rc;
                if( _data[pi].CompareTo( _data[ci] ) <= 0 ) break; // parent is smaller than (or equal to) smallest child so done
                T tmp = _data[pi]; _data[pi] = _data[ci]; _data[ci] = tmp; // swap parent and child
                pi = ci;
            }
            return frontItem;
        }

        public T Peek()
        {
            T frontItem = _data[0];
            return frontItem;
        }

        public int Count => _data.Count;

        public override string ToString()
        {
            string s = "";
            for( int i = 0; i < _data.Count; ++i )
                s += _data[i].ToString() + " ";
            s += "count = " + _data.Count;
            return s;
        }

        public bool IsConsistent()
        {
            // is the heap property true for all data?
            if( _data.Count == 0 ) return true;
            int li = _data.Count - 1; // last index
            for( int pi = 0; pi < _data.Count; ++pi ) // each parent index
            {
                int lci = 2 * pi + 1; // left child index
                int rci = 2 * pi + 2; // right child index

                if( lci <= li && _data[pi].CompareTo( _data[lci] ) > 0 ) return false; // if lc exists and it's greater than parent then bad.
                if( rci <= li && _data[pi].CompareTo( _data[rci] ) > 0 ) return false; // check the right child too.
            }
            return true; // passed all checks
        } // IsConsistent
    } // PriorityQueue

    struct MyMerge : IEquatable<MyMerge>, IComparable<MyMerge>
    {
        public MyMerge( int pos, int rank, int newId )
        {
            Pos = pos;
            Rank = rank;
            NewId = newId;
        }

        public int Pos { get; set; }
        public int Rank { get; set; }
        public int NewId { get; set; }

        public int CompareTo( MyMerge other )
        {
            if( Rank != other.Rank )
            {
                return Rank.CompareTo( other.Rank );
            }

            return Pos.CompareTo( other.Pos );
        }

        public override int GetHashCode()
        {
            int hashcode = 23;
            hashcode = (hashcode * 37) + Rank.GetHashCode();
            hashcode = (hashcode * 37) + Pos.GetHashCode();
            return hashcode;
        }

        public bool Equals( MyMerge other ) => Pos == other.Pos && Rank == other.Rank;
    }
}
