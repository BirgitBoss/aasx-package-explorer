﻿// SOURCE: https://github.com/OPCFoundation/UA-.NETStandard/blob/1.4.355.26/
// SampleApplications/Samples/NetCoreConsoleServer/Program.cs
// heavily modified

// TODO (MIHO, 2020-08-02): Andreas to check license!
// the (actual) Source is this: https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Applications/
// ConsoleReferenceServer/Program.cs
// it now features the MIT license!

/* Copyright (c) 1996-2019 The OPC Foundation. All rights reserved
   The source code in this file is covered under a dual-license scenario, which is
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AasOpcUaServer;
using AasxIntegrationBase;
using AdminShellNS;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Server;

namespace AasxUaNetServer
{

    public enum ExitCode
    {
        Ok = 0,
        ErrorServerNotStarted = 0x80,
        ErrorServerRunning = 0x81,
        ErrorServerException = 0x82,
        ErrorInvalidCommandLine = 0x100
    };

    public class UaServerWrapper
    {
        private LogInstance Log = null;
        SampleServer server;
        Task status;
        DateTime lastEventTime;
        int serverRunTime = Timeout.Infinite;
        static bool autoAccept = false;
        static ExitCode exitCode;
        static AdminShellPackageEnv aasxEnv = null;
        static AasxUaServerOptions aasxServerOptions = null;

        public UaServerWrapper(
            bool _autoAccept, int _stopTimeout, AdminShellPackageEnv _aasxEnv, LogInstance logger = null,
            AasxUaServerOptions _serverOptions = null)
        {
            autoAccept = _autoAccept;
            aasxEnv = _aasxEnv;
            aasxServerOptions = _serverOptions;
            serverRunTime = _stopTimeout == 0 ? Timeout.Infinite : _stopTimeout * 1000;
            this.Log = logger;
        }

        public void Run()
        {

            try
            {
                exitCode = ExitCode.ErrorServerNotStarted;
                Log.Info("will start..........");
                ConsoleSampleServer().Wait();
                Console.WriteLine("Server started.");
                exitCode = ExitCode.ErrorServerRunning;
            }
            catch (Exception ex)
            {
                Utils.Trace("ServiceResultException:" + ex.Message);
                Console.WriteLine("Exception: {0}", ex.Message);
                exitCode = ExitCode.ErrorServerException;
                return;
            }

            exitCode = ExitCode.Ok;
        }

        public void Stop()
        {
            if (server != null)
            {
                Console.WriteLine("Server stopped. Waiting for exit...");

                using (SampleServer _server = server)
                {
                    // Stop status thread
                    server = null;
                    if (status != null)
                        status.Wait();
                    // Stop server and dispose
                    if (_server != null)
                        _server.Stop();

                    Log.Info("End of Server stopping!");
                }
            }
        }

        public bool IsNotRunningAnymore()
        {
            if (status == null)
                return true;
            if (status.IsCanceled || status.IsCompleted || status.IsFaulted)
                return true;
            return false;
        }

        public static ExitCode ExitCode { get => exitCode; }

        private static void CertificateValidator_CertificateValidation(
            CertificateValidator validator, CertificateValidationEventArgs e)
        {
            if (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted)
            {
                e.Accept = autoAccept;
                if (autoAccept)
                {
                    Console.WriteLine("Accepted Certificate: {0}", e.Certificate.Subject);
                }
                else
                {
                    Console.WriteLine("Rejected Certificate: {0}", e.Certificate.Subject);
                }
            }
        }

        private async Task ConsoleSampleServer()
        {
            ApplicationInstance application = new ApplicationInstance();

            application.ApplicationName = "OPC UA AASX Server";
            application.ApplicationType = ApplicationType.Server;
            application.ConfigSectionName = Utils.IsRunningOnMono() ? "MonoAasxServerPlugin" : "Net46AasxServerPlugin";

            // modify ConfigSectionName with absoluet file?
            if (true)
            {
                string assemblyFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                application.ConfigSectionName = Path.Combine(assemblyFolder, application.ConfigSectionName);
            }

            // load the application configuration.
            ApplicationConfiguration config = await application.LoadApplicationConfiguration(false);

            // check the application certificate.
            bool haveAppCertificate = await application.CheckApplicationInstanceCertificate(false, 0);
            if (!haveAppCertificate)
            {
                throw new Exception("Application instance certificate invalid!");
            }

            if (!config.SecurityConfiguration.AutoAcceptUntrustedCertificates)
            {
                config.CertificateValidator.CertificateValidation +=
                    // ReSharper disable once RedundantDelegateCreation
                    new CertificateValidationEventHandler(CertificateValidator_CertificateValidation);
            }

            // Important: set appropriate trace mask
            Utils.SetTraceMask(Utils.TraceMasks.Error /* | Utils.TraceMasks.Information */
                | Utils.TraceMasks.StartStop /* | Utils.TraceMasks.StackTrace */);

            // attach tracing?
            Utils.Tracing.TraceEventHandler += (sender, args) =>
            {
                if (this.Log != null)
                {
                    // bad hack
                    if (args == null)
                        return;
                    if (args.TraceMask == 2 || args.TraceMask == 8 || args.TraceMask == 16)
                        return;

                    var st = String.Format(args.Format,
                        // ReSharper disable once CoVariantArrayConversion
                        // ReSharper disable once RedundantExplicitArrayCreation
                        (args.Arguments != null ? args.Arguments : new string[] { "" }));
                    this.Log.Info("[{0}] {1} {2} {3}",
                        args.TraceMask, st, args.Message, args.Exception?.Message ?? "-");
                }
            };

            // allow stopping after finalizing special jobs
            if (aasxServerOptions != null)
                aasxServerOptions.FinalizeAction += () =>
                {
                    server.Stop();
                };

            // start the server.
            server = new SampleServer(aasxEnv, aasxServerOptions);
            await application.Start(server);

            // start the status thread
            // ReSharper disable once RedundantDelegateCreation
            status = Task.Run(new Action(StatusThread));

            // print notification on session events
            server.CurrentInstance.SessionManager.SessionActivated += EventStatus;
            server.CurrentInstance.SessionManager.SessionClosing += EventStatus;
            server.CurrentInstance.SessionManager.SessionCreated += EventStatus;

        }

        private void EventStatus(Session session, SessionEventReason reason)
        {
            lastEventTime = DateTime.UtcNow;
            PrintSessionStatus(session, reason.ToString());
        }

        void PrintSessionStatus(Session session, string reason, bool lastContact = false)
        {
            lock (session.DiagnosticsLock)
            {
                string item = String.Format("{0,9}:{1,20}:", reason, session.SessionDiagnostics.SessionName);
                if (lastContact)
                {
                    item += String.Format("Last Event:{0:HH:mm:ss}",
                                session.SessionDiagnostics.ClientLastContactTime.ToLocalTime());
                }
                else
                {
                    if (session.Identity != null)
                    {
                        item += String.Format(":{0,20}", session.Identity.DisplayName);
                    }
                    item += String.Format(":{0}", session.Id);
                }
                Console.WriteLine(item);
            }
        }

        private async void StatusThread()
        {
            while (server != null)
            {
                if (DateTime.UtcNow - lastEventTime > TimeSpan.FromMilliseconds(6000))
                {
                    IList<Session> sessions = server.CurrentInstance.SessionManager.GetSessions();
                    // ReSharper disable once ForCanBeConvertedToForeach
                    for (int ii = 0; ii < sessions.Count; ii++)
                    {
                        Session session = sessions[ii];
                        PrintSessionStatus(session, "-Status-", true);
                    }
                    lastEventTime = DateTime.UtcNow;
                }
                await Task.Delay(1000);
            }
        }

    }
}
