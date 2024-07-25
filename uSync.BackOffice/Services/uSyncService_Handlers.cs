using NUglify.Helpers;
using Org.BouncyCastle.Asn1.Ocsp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using uSync.BackOffice.SyncHandlers;
using uSync.Core.Serialization;

namespace uSync.BackOffice
{
    /// <summary>
    ///  actions on individual handlers. 
    /// </summary>

    public partial class uSyncService
    {

        private IEnumerable<string> ItemsContent { get; set; }

        /// <summary>
        ///  Run a report for a given handler 
        /// </summary>
        public IEnumerable<uSyncAction> ReportHandler(string handler, uSyncImportOptions options)
        {
            var handlerPair = _handlerFactory.GetValidHandler(handler, new SyncHandlerOptions
            {
                Set = options.HandlerSet,
                Action = HandlerActions.Report
            });

            if (handlerPair == null) return Enumerable.Empty<uSyncAction>();
            var folder = GetHandlerFolder(options.RootFolder, handlerPair.Handler);

            return handlerPair.Handler.Report(folder, handlerPair.Settings, options.Callbacks?.Update);
        }

        /// <summary>
        ///  run an import for a given handler 
        /// </summary>
        public IEnumerable<uSyncAction> ImportHandler(string handlerAlias, uSyncImportOptions options, int lowerLimit, int jump)
        {

            lock (_importLock)
            {
                using (var pause = _mutexService.ImportPause())
                {
                    
                        var handlerPair = _handlerFactory.GetValidHandler(handlerAlias, new SyncHandlerOptions
                        {
                            Set = options.HandlerSet,
                            Action = HandlerActions.Import
                        });

                        if (handlerPair == null) return Enumerable.Empty<uSyncAction>();
                        var folder = GetHandlerFolder(options.RootFolder, handlerPair.Handler);

                        if (handlerAlias == "ContentHandler")
                        {
                            if (this.ItemsContent == null || !this.ItemsContent.Any())
                            {
                                this.ItemsContent = _syncFileService.GetFiles(folder, "");
                            }
                            IEnumerable<string> truncatedList = this.ItemsContent.Skip(lowerLimit).Take(jump);
                            truncatedList.ForEach(item =>
                            {
                                try
                                {
                                    var result = handlerPair.Handler.Import(item, handlerPair.Settings, false);
                                }
                                catch (Exception ex)
                                {
                                    
                                }
                            });
                            return truncatedList.Count() < jump ?  Enumerable.Empty<uSyncAction>() : null ;
                        }
                        else
                        {
                            return handlerPair.Handler.ImportAll(folder, handlerPair.Settings,
                                                       options.Flags.HasFlag(SerializerFlags.Force),
                                                       options.Callbacks?.Update);
                        }
                }
            }
        }

        /// <summary>
        ///  perform the post import actions for a handler 
        /// </summary>
        public IEnumerable<uSyncAction> PerformPostImport(string rootFolder, string handlerSet, IEnumerable<uSyncAction> actions)
        {
            lock (_importLock)
            {
                using (var pause = _mutexService.ImportPause())
                {
                    var handlers = _handlerFactory.GetValidHandlers(new SyncHandlerOptions { Set = handlerSet, Action = HandlerActions.Import });
                    return PerformPostImport(rootFolder, handlers, actions);
                }
            }
        }

        /// <summary>
        ///  run an export for a given handler 
        /// </summary>
        public IEnumerable<uSyncAction> ExportHandler(string handler, uSyncImportOptions options)
        {
            var handlerPair = _handlerFactory.GetValidHandler(handler, new SyncHandlerOptions
            {
                Set = options.HandlerSet,
                Action = HandlerActions.Export
            });

            if (handlerPair == null) return Enumerable.Empty<uSyncAction>();
            var folder = GetHandlerFolder(options.RootFolder, handlerPair.Handler);

            return handlerPair.Handler.ExportAll(folder, handlerPair.Settings, options.Callbacks?.Update);
        }

        /// <summary>
        ///  Start a bulk run, fires events, and for exports writes the version file.
        /// </summary>
        public void StartBulkProcess(HandlerActions action)
        {
            switch (action)
            {
                case HandlerActions.Export:
                    _mutexService.FireBulkStarting(new uSyncExportStartingNotification());
                    break;
                case HandlerActions.Import:
                    // cleans any caches we might have set.
                    _appCache.ClearByKey("usync_");

                    _mutexService.FireBulkStarting(new uSyncImportStartingNotification());
                    break;
                case HandlerActions.Report:
                    _mutexService.FireBulkStarting(new uSyncReportStartingNotification());
                    break;
            }
        }

        /// <summary>
        ///  Complete a bulk run, fire the event so other things know we have done it.
        /// </summary>
        public void FinishBulkProcess(HandlerActions action, IEnumerable<uSyncAction> actions)
        {
            switch (action)
            {
                case HandlerActions.Export:
                    WriteVersionFile(_uSyncConfig.GetRootFolder());
                    _mutexService.FireBulkComplete(new uSyncExportCompletedNotification(actions));
                    break;
                case HandlerActions.Import:
                    _mutexService.FireBulkComplete(new uSyncImportCompletedNotification(actions));
                    break;
                case HandlerActions.Report:
                    _mutexService.FireBulkComplete(new uSyncReportCompletedNotification(actions));
                    break;
            }
        }

        /// <summary>
        ///  gets the phyical folder for a handler. ( root + handlerfolder)
        /// </summary>
        private string GetHandlerFolder(string rootFolder, ISyncHandler handler)
            => Path.Combine(rootFolder, handler.DefaultFolder);

    }
}
