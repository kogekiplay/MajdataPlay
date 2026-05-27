using System;

namespace MajdataPlay.Recording
{
    internal class OBSRecorderException : Exception
    {
        public OBSRecorderException() : base()
        {

        }

        public OBSRecorderException(string message) : base(message)
        {

        }

        public OBSRecorderException(string message, Exception innerException) : base(message, innerException)
        {

        }
    }
}
