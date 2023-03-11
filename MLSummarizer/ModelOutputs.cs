using Microsoft.ML.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MLSummarizer
{
    public class ModelOutput
    {
        [VectorType( 1, 1024, 50264 )]
        [ColumnName( "logits" )]
        public float[] Logits { get; set; }

    }
}
