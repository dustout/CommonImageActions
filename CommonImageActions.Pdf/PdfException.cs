using System;
using System.Collections.Generic;
using System.Text;

namespace CommonImageActions.Pdf
{
    public class PdfException : Exception
    {
        public PdfException(string message)
        : base(message)
        { }
    }
}
