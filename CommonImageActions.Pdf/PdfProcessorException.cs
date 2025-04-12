using System;
using System.Collections.Generic;
using System.Text;

namespace CommonImageActions.Pdf
{
    public class PdfProcessorException : Exception
    {
        public PdfProcessorException(string message)
        : base(message)
        { }

        public PdfProcessorException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }
}
