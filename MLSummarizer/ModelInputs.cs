using Microsoft.ML.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MLSummarizer
{
    public class ModelInput
    {
        [VectorType( 1, 1024 )]
        [ColumnName( "input_ids" )]
        public long[] InputIds { get; set; }

        [VectorType( 1, 1024 )]
        [ColumnName( "attention_mask" )]
        public long[] AttentionMask { get; set; }

        [VectorType( 1, 1024 )]
        [ColumnName( "decoder_input_ids" )]
        public long[] DecoderInputIds { get; set; } = new long[1024];

        [VectorType( 1, 1024 )]
        [ColumnName( "decoder_attention_mask" )]
        public long[] DecoderAttentionMask { get; set; } = new long[1024];

    }

}
