using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace QtisVisionPanel
{
    public static class TaskExtensions
    {
        public static void SafeFireAndForget(
            this Task task,
            Action<Exception> onException)
        {
            task.SafeFireAndForget(true, onException);
        }

        public static async void SafeFireAndForget(
            this Task task,
            bool continueOnCapturedContext = true,
            Action<Exception> onException = null,
            [CallerMemberName] string caller = "")
        {
            try
            {
                await task.ConfigureAwait(continueOnCapturedContext);
            }
            catch (Exception ex)
            {
                onException?.Invoke(ex);
                NLog.LogManager.GetCurrentClassLogger().Error(ex, $"Unhandled exception in {caller}");
            }
        }
    }
}
