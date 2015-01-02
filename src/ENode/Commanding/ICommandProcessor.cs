﻿namespace ENode.Commanding
{
    /// <summary>Represents a processor to process command.
    /// </summary>
    public interface ICommandProcessor
    {
        /// <summary>Start the processor.
        /// </summary>
        void Start();
        /// <summary>Process the given command.
        /// </summary>
        /// <param name="processingCommand"></param>
        void Process(ProcessingCommand processingCommand);
    }
}
