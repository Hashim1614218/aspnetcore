﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNet.Razor.Text;

namespace Microsoft.AspNet.Razor.Generator.Compiler
{
    public class HelperChunk : ChunkBlock
    {
        public LocationTagged<string> Signature { get; set; }
        public LocationTagged<string> Footer { get; set; }
        // TODO: Can these properties be taken out?
        public bool HeaderComplete { get; set; }
    }
}
