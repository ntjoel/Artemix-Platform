using Cognex.VisionPro.ToolBlock;
using System;

namespace QtisVisionPanel.Services
{
    /// <summary>
    /// Captures the root ToolBlock outputs that belong to one VisionPro
    /// UserResultAvailable event. The live job ToolBlock is reused by VisionPro and
    /// may already contain the next frame by the time a queued result is validated.
    /// </summary>
    internal static class VisionToolBlockOutputSnapshot
    {
        public static CogToolBlock Capture(CogToolBlock source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var snapshot = new CogToolBlock
            {
                Name = string.IsNullOrWhiteSpace(source.Name)
                    ? "ResultOutputSnapshot"
                    : source.Name + "_ResultOutputSnapshot"
            };

            try
            {
                if (source.Outputs == null)
                {
                    return snapshot;
                }

                for (int index = 0; index < source.Outputs.Count; index++)
                {
                    CogToolBlockTerminal sourceTerminal = source.Outputs[index];
                    if (sourceTerminal == null || string.IsNullOrWhiteSpace(sourceTerminal.Name))
                    {
                        continue;
                    }

                    Type valueType = sourceTerminal.ValueType ?? typeof(object);
                    object value = CopyStableValue(sourceTerminal.Value);
                    var snapshotTerminal = value != null
                        ? new CogToolBlockTerminal(sourceTerminal.Name, value, valueType)
                        : new CogToolBlockTerminal(sourceTerminal.Name, valueType);

                    snapshotTerminal.Description = sourceTerminal.Description;
                    snapshot.Outputs.Add(snapshotTerminal);
                }

                return snapshot;
            }
            catch
            {
                snapshot.Dispose();
                throw;
            }
        }

        private static object CopyStableValue(object value)
        {
            if (value == null)
            {
                return null;
            }

            Type valueType = value.GetType();
            if (valueType.IsValueType || value is string)
            {
                return value;
            }

            if (value is byte[] bytes)
            {
                return (byte[])bytes.Clone();
            }

            // Images and Cognex result objects remain owned by the immutable user
            // record. Validators consume scalar outputs only, so retaining a mutable
            // live reference here would defeat the purpose of the snapshot.
            return null;
        }
    }
}
