using Cognex.VisionPro.QuickBuild;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Cognex.VisionPro.QuickBuild.CogJobManager;

namespace QtisVisionPanel.Cls_Vpro
{
    public class CognexEventManager
    {
        private readonly ICognexJobManager _cognexManager;
        private bool _eventsRegistered = false;

        public CognexEventManager(ICognexJobManager cognexManager)
        {
            _cognexManager = cognexManager;
        }

        public void RegisterEvents(
           CogUserResultAvailableEventHandler userResultHandler,
            EventHandler<CogJobManagerActionEventArgs> jobStoppedHandler)
        {
            if (_eventsRegistered) return;

            _cognexManager.UserResultAvailable -= userResultHandler;
            _cognexManager.UserResultAvailable += userResultHandler;

            _cognexManager.JobStopped -= jobStoppedHandler;
            _cognexManager.JobStopped += jobStoppedHandler;

            _eventsRegistered = true;
            MainWindow.logger.Debug("Eventi Cognex registrati.");
        }

        public void UnregisterEvents(
           CogUserResultAvailableEventHandler userResultHandler,
            EventHandler<CogJobManagerActionEventArgs> jobStoppedHandler)
        {
            if (!_eventsRegistered) return;

            _cognexManager.UserResultAvailable -= userResultHandler;
            _cognexManager.JobStopped -= jobStoppedHandler;

            _eventsRegistered = false;
            MainWindow.logger.Debug("Eventi Cognex deregistrati.");
        }
    }
}
