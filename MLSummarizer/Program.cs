using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using System.Linq;

namespace MLSummarizer
{
    class Program
    {

        static void Main( string[] args )
        {
            var mlContext = new MLContext();
            var path = "model.onnx";
            bool useGpu = false;
            var tokenizer = new Tokenizer( new Bpe( "vocab.json", null, continuingSubwordPrefix: "Ġ" ) );
            var pipeline = mlContext.Transforms
                            .ApplyOnnxModel( modelFile: path,
                                            shapeDictionary: new Dictionary<string, int[]>
                                            {
                                                { "input_ids", new [] { 1, 1024 } },
                                                { "attention_mask", new [] { 1, 1024 } },
                                                { "decoder_input_ids", new [] { 1, 1024 } },
                                                { "decoder_attention_mask", new [] { 1, 1024 } },
                                                { "logits", new [] { 1, 1024, 50264 } },
                                                //{ "onnx::MatMul_2118", new [] { 1, 32, 1024 } },
                                            },
                                            inputColumnNames: new[] {"input_ids",
                                                                     "attention_mask",
                                                               "decoder_input_ids",
                                                               "decoder_attention_mask"
                                                                },
                                            outputColumnNames: new[] { "logits",
                                                              //"onnx::MatMul_2118"
                                            },
                                            gpuDeviceId: useGpu ? 0 : (int?)null,
                                            fallbackToCpu: true );

            var transformer = pipeline.Fit( mlContext.Data.LoadFromEnumerable( new List<ModelInput>() ) );
            var tokenized = tokenizer.Encode( @"Tim: Hi, what's up? Kim: Bad mood tbh, I was going to do lots of stuff but ended up procrastinating Tim: What did you plan on doing? Kim: Oh you know, uni stuff and unfucking my room Kim: Maybe tomorrow I'll move my ass and do everything Kim: We were going to defrost a fridge so instead of shopping I'll eat some defrosted veggies Tim: For doing stuff I recommend Pomodoro technique where u use breaks for doing chores Tim: It really helps Kim: thanks, maybe I'll do that Tim: I also like using post-its in kaban style" );
            var encoderTokenized = tokenizer.Encode( "Summarize the text." );
            var result = "Kim may try the pomodoro technique recommended by Tim to get more stuff done.";
            var tokenCount = 1024;
            var tokenToPad = tokenCount - tokenized.Ids.Count;
            var encoderTokenToPad = tokenCount - encoderTokenized.Ids.Count;
            var tokens = tokenized.Ids
                    .Select( s => (long)s )
                    .Concat( Enumerable.Repeat( 0L, tokenToPad ) )
                    .ToArray();
            var encoderTokens = encoderTokenized.Ids
                    .Select( s => (long)s )
                    .Concat( Enumerable.Repeat( 0L, encoderTokenToPad ) )
                    .ToArray();
            var attentionMask = Enumerable.Repeat( 1L, tokenized.Tokens.Count )
                .Concat( Enumerable.Repeat( 0L, tokenToPad ) )
                .ToArray();
            var encoderAttentionMask = Enumerable.Repeat( 1L, encoderTokenized.Tokens.Count )
                .Concat( Enumerable.Repeat( 0L, encoderTokenToPad ) )
                .ToArray();
            var input = new ModelInput()
            {
                AttentionMask = attentionMask,
                InputIds = tokens,
                DecoderAttentionMask = encoderAttentionMask,
                DecoderInputIds = encoderTokens
            };

            var predictionEngine = mlContext.Model.CreatePredictionEngine<ModelInput, ModelOutput>( transformer );

            var output = predictionEngine.Predict( input );
            float[] logits = output.Logits;
            
            // Find the index of the maximum value in the logits array
            // Get the index of the highest-scoring token for each position in the output sequence
            var outTokens = logits
                .Select( ( s, i ) => (s, i) )
                .GroupBy( ( s ) => (s.i / 50264 ) ).Select( s =>
            {
                var arr = s.Select( s => s.s ).ToArray();
                arr = Softmax( arr );
                return Array.IndexOf( arr, arr.Max() );
            } ).ToArray();
            var decoded = tokenizer.Decode( outTokens, true );
            Console.WriteLine( decoded.Replace( "Ġ", "") );
        }


        static float[] Softmax( float[] values )
        {
            var maxVal = values.Max();
            var exp = values.Select( v => Math.Exp( v - maxVal ) );
            var sumExp = exp.Sum();

            return exp.Select( v => (float)(v / sumExp) ).ToArray();
        }

        static T[,] Make2DArray<T>( T[] input, int height, int width )
        {
            T[,] output = new T[height, width];
            for( int i = 0; i < height; i++ )
            {
                for( int j = 0; j < width; j++ )
                {
                    output[i, j] = input[i * width + j];
                }
            }
            return output;
        }
    }
}
