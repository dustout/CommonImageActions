using System;
using System.Collections.Generic;
using System.Text;

namespace CommonImageActions.Core
{
    public class ImageProcessorException:Exception
    {
        public ImageProcessorException(string message)
                    : base(message)
        { }
        public ImageProcessorException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }
}
