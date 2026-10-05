using NLog;
using System;
using System.Threading.Tasks;

namespace QtisVisionPanel.Extensions
{
    public static class TaskExtensions
    {
        private static readonly Logger _logger = LogManager.GetLogger("TaskExtensions");

        /// <summary>
        /// Avvia una task fire-and-forget garantendo che le eccezioni vengano loggate
        /// invece di essere inghiottite silenziosamente.
        /// </summary>
        public static void SafeFireAndForget(this Task task, Action<Exception> onException = null, string context = null)
        {
            task.ContinueWith(t =>
            {
                var ex = t.Exception?.InnerException ?? t.Exception;
                if (ex == null) return;

                if (onException != null)
                {
                    onException(ex);
                }
                else
                {
                    string ctx = string.IsNullOrEmpty(context) ? "fire-and-forget task" : context;
                    _logger.Error(ex, $"UNHANDLED_TASK_EXCEPTION|context={ctx}");
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        /// <summary>
        /// Versione con logger e contesto espliciti per i path critici (IO, allarmi).
        /// </summary>
        public static void SafeFireAndForget(this Task task, Logger logger, string eventCode)
        {
            task.ContinueWith(t =>
            {
                var ex = t.Exception?.InnerException ?? t.Exception;
                if (ex != null)
                    logger.Error(ex, eventCode);
            }, TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
