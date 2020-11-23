﻿using System;
using System.Collections.Generic;
using System.Configuration;
using log4net;
using Microsoft.Exchange.WebServices.Data;

namespace Mail2Bug.Email.EWS
{
    /// <summary>
    /// This class caches EWS connection objects based on their settings.
    /// When a caller asks for a new EWS connection, if an appropriate object already exists, we just return that object
    /// thus avoiding the long initialization time for EWS (~1 minute).
    /// 
    /// This works well for configurations where many InstanceConfigs are relying on the same user with different mail folders.
    /// 
    /// Since we may want to be able to turn off caching in some cases, the caching itself is controlled at initialization time.
    /// If caching is disabled, a new connection will be initiated for every call.
    /// </summary>
    public class EWSConnectionManger
    {
        public struct Credentials
        {
            public string EmailAddress;
            public string UserName;
            public string Password;
        }

        public struct EWSConnection
        {
            public ExchangeService Service;
            public RecipientsMailboxManagerRouter Router;
        }

        public EWSConnectionManger(bool enableConnectionCaching)
        {
            _enableConnectionCaching = enableConnectionCaching;

            if (_enableConnectionCaching)
            {
                _cachedConnections = new Dictionary<Tuple<string, string, int, bool>, EWSConnection>();
            }
        }

        public EWSConnection GetConnection(Credentials credentials, bool useConversationGuidOnly)
        {
            if (!_enableConnectionCaching)
            {
                return ConnectToEWS(credentials, useConversationGuidOnly);
            }

            lock (_cachedConnections)
            {
                var key = GetKeyFromCredentials(credentials, useConversationGuidOnly);

                if (_cachedConnections.ContainsKey(key))
                {
                    Logger.InfoFormat("FolderMailboxManager for {0} already exists - retrieving from cache", key);
                    return _cachedConnections[key];
                }

                Logger.InfoFormat("Creating FolderMailboxManager for {0}", key);
                _cachedConnections[key] = ConnectToEWS(credentials, useConversationGuidOnly);
                return _cachedConnections[key];
            }
        }

        public void ProcessInboxes()
        {
            foreach (var connection in _cachedConnections)
            {
                Logger.InfoFormat("Processing inbox for connection {0}", connection.Key);
                connection.Value.Router.ProcessInbox();
            }
        }

        static private Tuple<string, string, int, bool> GetKeyFromCredentials(Credentials credentials, bool useConversationGuid)
        {
            return new Tuple<string, string, int, bool>(
                credentials.EmailAddress,
                credentials.UserName, credentials.Password.GetHashCode(), useConversationGuid);
        }

        static private EWSConnection ConnectToEWS(Credentials credentials, bool useConversationGuidOnly)
        {
            Logger.DebugFormat("Initializing FolderMailboxManager for email adderss {0}", credentials.EmailAddress);
            var exchangeService = new ExchangeService(ExchangeVersion.Exchange2010_SP1)
            {
                Credentials = new WebCredentials(credentials.UserName, credentials.Password),
                Timeout = 60000
            };

            /*exchangeService.AutodiscoverUrl(
                credentials.EmailAddress,
                x =>
                {
                    Logger.DebugFormat("Following redirection for EWS autodiscover: {0}", x);
                    return true;
                }
                );*/

            // https://github.com/microsoft/mail2bug/issues/64
            exchangeService.Url = new Uri("https://webmail.3imedia.de/EWS/Exchange.asmx");

            Logger.DebugFormat("Service URL: {0}", exchangeService.Url);

            return new EWSConnection()
            {
                Service = exchangeService,
                Router =
                    new RecipientsMailboxManagerRouter(
                        new EWSMailFolder(Folder.Bind(exchangeService, WellKnownFolderName.Inbox), useConversationGuidOnly))
            };
        }


        private readonly Dictionary<Tuple<string, string, int, bool>, EWSConnection> _cachedConnections;
        private readonly bool _enableConnectionCaching;

        private static readonly ILog Logger = LogManager.GetLogger(typeof(EWSConnectionManger));
    }
}
