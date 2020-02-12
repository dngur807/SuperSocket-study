﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Runtime.Serialization.Formatters;
using System.Text;
using System.Threading;
using SuperSocket.Common;
using SuperSocket.SocketBase;
using SuperSocket.SocketBase.Config;
using SuperSocket.SocketBase.Logging;
using SuperSocket.SocketBase.Metadata;
using SuperSocket.SocketBase.Provider;
using SuperSocket.SocketEngine.Configuration;

namespace SuperSocket.SocketEngine
{
    /// <summary>
    /// SuperSocket default bootstrap
    /// </summary>
    public partial class DefaultBootstrap : IBootstrap, ILoggerProvider, IDisposable
    {
        private List<IWorkItem> m_AppServers;

        private IWorkItem m_ServerManager;

        /// <summary>
        /// Indicates whether the bootstrap is initialized
        /// </summary>
        private bool m_Initialized = false;

        /// <summary>
        /// Global configuration
        /// </summary>
        private IConfigurationSource m_Config;

        /// <summary>
        /// Global log
        /// </summary>
        private ILog m_GlobalLog;

        /// <summary>
        /// Gets the bootstrap logger.
        /// </summary>
        ILog ILoggerProvider.Logger
        {
            get { return m_GlobalLog; }
        }

        /// <summary>
        /// Gets the log factory.
        /// </summary>
        protected ILogFactory LogFactory { get; private set; }

        /// <summary>
        /// Gets all the app servers running in this bootstrap
        /// </summary>
        public IEnumerable<IWorkItem> AppServers
        {
            get { return m_AppServers; }
        }

        private readonly IRootConfig m_RootConfig;

        /// <summary>
        /// Gets the config.
        /// </summary>
        public IRootConfig Config
        {
            get
            {
                if (m_Config != null)
                    return m_Config;

                return m_RootConfig;
            }
        }

        /// <summary>
        /// Gets the startup config file.
        /// </summary>
        public string StartupConfigFile { get; private set; }

        /// <summary>
        /// Gets the <see cref="PerformanceMonitor"/> class.
        /// </summary>
        public IPerformanceMonitor PerfMonitor { get { return m_PerfMonitor; } }

        private PerformanceMonitor m_PerfMonitor;

        private readonly string m_BaseDirectory = AppDomain.CurrentDomain.BaseDirectory;

        /// <summary>
        /// Gets the base directory.
        /// </summary>
        /// <value>
        /// The base directory.
        /// </value>
        public string BaseDirectory
        {
            get
            {
                return m_BaseDirectory;
            }
        }

        partial void SetDefaultCulture(IRootConfig rootConfig);

        /// <summary>
        /// Initializes a new instance of the <see cref="DefaultBootstrap"/> class.
        /// </summary>
        /// <param name="appServers">The app servers.</param>
        public DefaultBootstrap(IEnumerable<IWorkItem> appServers)
            : this(new RootConfig(), appServers, new NLogLogFactory()) 
        {

        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DefaultBootstrap"/> class.
        /// </summary>
        /// <param name="rootConfig">The root config.</param>
        /// <param name="appServers">The app servers.</param>
        public DefaultBootstrap(IRootConfig rootConfig, IEnumerable<IWorkItem> appServers)
            : this(rootConfig, appServers, new NLogLogFactory())  
        {

        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DefaultBootstrap"/> class.
        /// </summary>
        /// <param name="rootConfig">The root config.</param>
        /// <param name="appServers">The app servers.</param>
        /// <param name="logFactory">The log factory.</param>
        public DefaultBootstrap(IRootConfig rootConfig, IEnumerable<IWorkItem> appServers, ILogFactory logFactory)
        {
            if (rootConfig == null)
                throw new ArgumentNullException("rootConfig");

            if (appServers == null)
                throw new ArgumentNullException("appServers");

            if(!appServers.Any())
                throw new ArgumentException("appServers must have one item at least", "appServers");

            if (logFactory == null)
                throw new ArgumentNullException("logFactory");

            m_RootConfig = rootConfig;

            SetDefaultCulture(rootConfig);

            m_AppServers = appServers.ToList();

            m_GlobalLog = logFactory.GetLog(this.GetType().Name);

            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);

            if (!rootConfig.DisablePerformanceDataCollector)
            {
                m_PerfMonitor = new PerformanceMonitor(rootConfig, m_AppServers, null, logFactory);

                if (m_GlobalLog.IsDebugEnabled)
                    m_GlobalLog.Debug("The PerformanceMonitor has been initialized!");
            }

            if (m_GlobalLog.IsDebugEnabled)
                m_GlobalLog.Debug("The Bootstrap has been initialized!");

            m_Initialized = true;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DefaultBootstrap"/> class.
        /// </summary>
        /// <param name="config">The config.</param>
        public DefaultBootstrap(IConfigurationSource config)
        {
            if (config == null)
                throw new ArgumentNullException("config");

            SetDefaultCulture(config);

            var fileConfigSource = config as ConfigurationSection;

            if (fileConfigSource != null)
                StartupConfigFile = fileConfigSource.GetConfigSource();

            m_Config = config;

            AppDomain.CurrentDomain.SetData("Bootstrap", this);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DefaultBootstrap"/> class.
        /// </summary>
        /// <param name="config">The config.</param>
        /// <param name="startupConfigFile">The startup config file.</param>
        public DefaultBootstrap(IConfigurationSource config, string startupConfigFile)
        {
            if (config == null)
                throw new ArgumentNullException("config");

            SetDefaultCulture(config);

            if (!string.IsNullOrEmpty(startupConfigFile))
                StartupConfigFile = startupConfigFile;

            m_Config = config;

            AppDomain.CurrentDomain.SetData("Bootstrap", this);
        }

        /// <summary>
        /// Creates the work item instance.
        /// </summary>
        /// <param name="serviceTypeName">Name of the service type.</param>
        /// <param name="serverStatusMetadata">The server status metadata.</param>
        /// <returns></returns>
        protected virtual IWorkItem CreateWorkItemInstance(string serviceTypeName, StatusInfoAttribute[] serverStatusMetadata)
        {
            var serviceType = Type.GetType(serviceTypeName, true);
            return Activator.CreateInstance(serviceType) as IWorkItem;
        }

        internal virtual bool SetupWorkItemInstance(IWorkItem workItem, WorkItemFactoryInfo factoryInfo)
        {
            try
            {
                //Share AppDomain AppServers also share same socket server factory and log factory instances
                factoryInfo.SocketServerFactory.ExportFactory.EnsureInstance();
                factoryInfo.LogFactory.ExportFactory.EnsureInstance();
            }
            catch (Exception e)
            {
                if (m_GlobalLog.IsErrorEnabled)
                    m_GlobalLog.Error(e);

                return false;
            }

            return workItem.Setup(this, factoryInfo.Config, factoryInfo.ProviderFactories.ToArray());
        }

        /// <summary>
        /// Gets the work item factory info loader.
        /// </summary>
        /// <param name="config">The config.</param>
        /// <param name="logFactory">The log factory.</param>
        /// <returns></returns>
        internal virtual WorkItemFactoryInfoLoader GetWorkItemFactoryInfoLoader(IConfigurationSource config, ILogFactory logFactory)
        {
            return new WorkItemFactoryInfoLoader(config, logFactory);
        }

        /// <summary>
        /// Initializes the bootstrap with a listen endpoint replacement dictionary
        /// </summary>
        /// <param name="listenEndPointReplacement">The listen end point replacement.</param>
        /// <returns></returns>
        public virtual bool Initialize(IDictionary<string, IPEndPoint> listenEndPointReplacement)
        {
            return Initialize((c) => ReplaceListenEndPoint(c, listenEndPointReplacement));
        }

        private IServerConfig ReplaceListenEndPoint(IServerConfig serverConfig, IDictionary<string, IPEndPoint> listenEndPointReplacement)
        {
            var config = new ServerConfig(serverConfig);

            if (serverConfig.Port > 0)
            {
                var endPointKey = serverConfig.Name + "_" + serverConfig.Port;

                IPEndPoint instanceEndpoint;

                if(!listenEndPointReplacement.TryGetValue(endPointKey, out instanceEndpoint))
                {
                    throw new Exception(string.Format("Failed to find Input Endpoint configuration {0}!", endPointKey));
                }

                config.Ip = instanceEndpoint.Address.ToString();
                config.Port = instanceEndpoint.Port;
            }

            if (config.Listeners != null && config.Listeners.Any())
            {
                var listeners = config.Listeners.ToArray();

                for (var i = 0; i < listeners.Length; i++)
                {
                    var listener = (ListenerConfig)listeners[i];

                    var endPointKey = serverConfig.Name + "_" + listener.Port;

                    IPEndPoint instanceEndpoint;

                    if (!listenEndPointReplacement.TryGetValue(endPointKey, out instanceEndpoint))
                    {
                        throw new Exception(string.Format("Failed to find Input Endpoint configuration {0}!", endPointKey));
                    }

                    listener.Ip = instanceEndpoint.Address.ToString();
                    listener.Port = instanceEndpoint.Port;
                }

                config.Listeners = listeners;
            }

            return config;
        }

        private IWorkItem InitializeAndSetupWorkItem(WorkItemFactoryInfo factoryInfo)
        {
            IWorkItem appServer;

            try
            {
                appServer = CreateWorkItemInstance(factoryInfo.ServerType, factoryInfo.StatusInfoMetadata);

                if (m_GlobalLog.IsDebugEnabled)
                    m_GlobalLog.DebugFormat("The server instance {0} has been created!", factoryInfo.Config.Name);
            }
            catch (Exception e)
            {
                if (m_GlobalLog.IsErrorEnabled)
                    m_GlobalLog.Error(string.Format("Failed to create server instance {0}!", factoryInfo.Config.Name), e);
                return null;
            }

            var exceptionSource = appServer as IExceptionSource;

            if (exceptionSource != null)
                exceptionSource.ExceptionThrown += new EventHandler<ErrorEventArgs>(exceptionSource_ExceptionThrown);


            var setupResult = false;

            try
            {
                setupResult = SetupWorkItemInstance(appServer, factoryInfo);

                if (m_GlobalLog.IsDebugEnabled)
                    m_GlobalLog.DebugFormat("The server instance {0} has been initialized!", appServer.Name);
            }
            catch (Exception e)
            {
                m_GlobalLog.Error(e);
                setupResult = false;
            }

            if (!setupResult)
            {
                if (m_GlobalLog.IsErrorEnabled)
                    m_GlobalLog.Error("Failed to setup server instance!");

                return null;
            }

            return appServer;
        }


        /// <summary>
        /// Initializes the bootstrap with the configuration, config resolver and log factory.
        /// </summary>
        /// <param name="serverConfigResolver">The server config resolver.</param>
        /// <param name="logFactory">The log factory.</param>
        /// <returns></returns>
        public virtual bool Initialize(Func<IServerConfig, IServerConfig> serverConfigResolver, ILogFactory logFactory)
        {
            if (m_Initialized)
                throw new Exception("The server had been initialized already, you cannot initialize it again!");

            if (logFactory != null && !string.IsNullOrEmpty(m_Config.LogFactory))
            {
                throw new ArgumentException("You cannot pass in a logFactory parameter, if you have configured a root log factory.", "logFactory");
            }

            IEnumerable<WorkItemFactoryInfo> workItemFactories;

            using (var factoryInfoLoader = GetWorkItemFactoryInfoLoader(m_Config, logFactory))
            {
                var bootstrapLogFactory = factoryInfoLoader.GetBootstrapLogFactory();

                logFactory = bootstrapLogFactory.ExportFactory.CreateExport<ILogFactory>();

                LogFactory = logFactory;
                m_GlobalLog = logFactory.GetLog(this.GetType().Name);

                AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);

                try
                {
                    workItemFactories = factoryInfoLoader.LoadResult(serverConfigResolver);
                }
                catch (Exception e)
                {
                    if (m_GlobalLog.IsErrorEnabled)
                        m_GlobalLog.Error(e);

                    return false;
                }
            }

            m_AppServers = new List<IWorkItem>(m_Config.Servers.Count());

            IWorkItem serverManager = null;

            //Initialize servers
            foreach (var factoryInfo in workItemFactories)
            {
                IWorkItem appServer = InitializeAndSetupWorkItem(factoryInfo);

                if (appServer == null)
                    return false;

                if (factoryInfo.IsServerManager)
                {
                    serverManager = appServer;
                }
                

                m_AppServers.Add(appServer);
            }

            if (serverManager != null)
                m_ServerManager = serverManager;

            if (!m_Config.DisablePerformanceDataCollector)
            {
                m_PerfMonitor = new PerformanceMonitor(m_Config, m_AppServers, serverManager, logFactory);

                if (m_GlobalLog.IsDebugEnabled)
                    m_GlobalLog.Debug("The PerformanceMonitor has been initialized!");
            }

            if (m_GlobalLog.IsDebugEnabled)
                m_GlobalLog.Debug("The Bootstrap has been initialized!");

            //try
            //{
            //    RegisterRemotingService();
            //}
            //catch (Exception e)
            //{
            //    if (m_GlobalLog.IsErrorEnabled)
            //        m_GlobalLog.Error("Failed to register remoting access service!", e);

            //    return false;
            //}

            m_Initialized = true;

            return true;
        }

        void exceptionSource_ExceptionThrown(object sender, ErrorEventArgs e)
        {
            m_GlobalLog.Error(string.Format("The server {0} threw an exception.", ((IWorkItemBase)sender).Name), e.Exception);
        }

        void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            m_GlobalLog.Error("The process crashed for an unhandled exception!", (Exception)e.ExceptionObject);
        }

        /// <summary>
        /// Initializes the bootstrap with the configuration and config resolver.
        /// </summary>
        /// <param name="serverConfigResolver">The server config resolver.</param>
        /// <returns></returns>
        public virtual bool Initialize(Func<IServerConfig, IServerConfig> serverConfigResolver)
        {
            return Initialize(serverConfigResolver, null);
        }

        /// <summary>
        /// Initializes the bootstrap with the configuration
        /// </summary>
        /// <param name="logFactory">The log factory.</param>
        /// <returns></returns>
        public virtual bool Initialize(ILogFactory logFactory)
        {
            return Initialize(c => c, logFactory);
        }

        /// <summary>
        /// Initializes the bootstrap with the configuration
        /// </summary>
        /// <returns></returns>
        public virtual bool Initialize()
        {
            return Initialize(c => c);
        }

        /// <summary>
        /// Starts this bootstrap.
        /// </summary>
        /// <returns></returns>
        public StartResult Start()
        {
            if (!m_Initialized)
            {
                if (m_GlobalLog.IsErrorEnabled)
                    m_GlobalLog.Error("You cannot invoke method Start() before initializing!");

                return StartResult.Failed;
            }

            var result = StartResult.None;

            var succeeded = 0;

            foreach (var server in m_AppServers)
            {
                if (!server.Start())
                {
                    if (m_GlobalLog.IsErrorEnabled)
                        m_GlobalLog.InfoFormat("The server instance {0} has failed to be started!", server.Name);
                }
                else
                {
                    succeeded++;                    
                }
            }

            if (m_AppServers.Any())
            {
                if (m_AppServers.Count == succeeded)
                    result = StartResult.Success;
                else if (succeeded == 0)
                    result = StartResult.Failed;
                else
                    result = StartResult.PartialSuccess;
            }

            if (m_PerfMonitor != null)
            {
                m_PerfMonitor.Start();

                if (m_GlobalLog.IsDebugEnabled)
                    m_GlobalLog.Debug("The PerformanceMonitor has been started!");
            }

            return result;
        }

        /// <summary>
        /// Stops this bootstrap.
        /// </summary>
        public void Stop()
        {
            var servers = m_AppServers.ToArray();

            if (servers.Any(s => s.Config != null && s.Config.StartupOrder != 0))
            {
                Array.Reverse(servers);
            }

            foreach (var server in servers)
            {
                if (server.State == ServerState.Running)
                {
                    server.Stop();                    
                }
            }

            if (m_PerfMonitor != null)
            {
                m_PerfMonitor.Stop();

                if (m_GlobalLog.IsDebugEnabled)
                    m_GlobalLog.Debug("The PerformanceMonitor has been stoppped!");
            }
        }

        
        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                AppDomain.CurrentDomain.UnhandledException -= new UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);
            }
        }

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void ResetPerfMoniter()
        {
            if (m_PerfMonitor != null)
            {
                m_PerfMonitor.Stop();
                m_PerfMonitor.Dispose();
                m_PerfMonitor = null;
            }

            m_PerfMonitor = new PerformanceMonitor(m_Config, m_AppServers, m_ServerManager, LogFactory);
            m_PerfMonitor.Start();

            if (m_GlobalLog.IsDebugEnabled)
                m_GlobalLog.Debug("The PerformanceMonitor has been reset for new server has been added!");
        }


        // 4.5
        partial void SetDefaultCulture(SocketBase.Config.IRootConfig rootConfig)
        {
            if (!string.IsNullOrEmpty(rootConfig.DefaultCulture))
                CultureInfo.DefaultThreadCurrentCulture = new CultureInfo(rootConfig.DefaultCulture);
        }

        // 4.0
        IWorkItem AddNewServer(IServerConfig config)
        {
            if (config == null)
                throw new ArgumentNullException("config");

            if (string.IsNullOrEmpty(config.Name))
                throw new ArgumentException("The new server's name cannot be empty.", "config");

            if (!m_Initialized)
                throw new Exception("The bootstrap must be initialized already!");

            if (m_AppServers.Any(s => config.Name.Equals(s.Name, StringComparison.OrdinalIgnoreCase)))
            {
                m_GlobalLog.ErrorFormat("The new server's name '{0}' has been taken by another server.", config.Name);
                return null;
            }

            var configSource = new ConfigurationSource(m_Config);
            configSource.Servers = new IServerConfig[] { new ServerConfig(config) };

            IEnumerable<WorkItemFactoryInfo> workItemFactories;

            using (var factoryInfoLoader = GetWorkItemFactoryInfoLoader(configSource, null))
            {
                try
                {
                    workItemFactories = factoryInfoLoader.LoadResult((c) => c);
                }
                catch (Exception e)
                {
                    if (m_GlobalLog.IsErrorEnabled)
                        m_GlobalLog.Error(e);

                    return null;
                }
            }

            var server = InitializeAndSetupWorkItem(workItemFactories.FirstOrDefault());

            if (server != null)
            {
                m_AppServers.Add(server);

                if (!m_Config.DisablePerformanceDataCollector)
                {
                    ResetPerfMoniter();
                }

                var section = m_Config as SocketServiceConfig;

                if (section != null) //file configuration
                {
                    var serverConfig = new Server();
                    serverConfig.LoadFrom(config);
                    section.Servers.AddNew(serverConfig);
                    ConfigurationWatcher.Pause();
                    section.GetCurrentConfiguration().Save(ConfigurationSaveMode.Minimal);
                    ConfigurationWatcher.Resume();
                }
            }

            return server;
        }
                
    }
}
