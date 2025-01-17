﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BPUtil;

namespace MonitorControl
{
	public class MonitorControlService
	{
		static MonitorControlService()
		{
			CertificateValidation.RegisterCallback(CertificateValidation.DoNotValidate_ValidationCallback);
		}

		MonitorControlServer httpServer;
		/// <summary>
		/// An event which is raised when the http server binds a socket, indicating that a listening port number may have changed.
		/// </summary>
		public event EventHandler<string> SocketBound = delegate { };

		/// <summary>
		/// Currently active http port.
		/// </summary>
		public int http_port { get; private set; }
		/// <summary>
		/// Currently active https port.
		/// </summary>
		public int https_port { get; private set; }

		Thread thrSyncWithOtherServer;
		/// <summary>
		/// A human-readable string describing the status of the server we are synchronizing with.
		/// </summary>
		public static string syncedServerStatus = "Remote server sync engine is loading …";

		public MonitorControlService()
		{
			BPUtil.SimpleHttp.SimpleHttpLogger.RegisterLogger(BPUtil.Logger.httpLogger);
		}
		/// <summary>
		/// Stops and then starts the service.
		/// </summary>
		public void Start()
		{
			Stop();
			Logger.StartLoggingThreads();
			http_port = -1;
			https_port = -1;
			httpServer = new MonitorControlServer(Program.settings.http_port, Program.settings.https_port);
			httpServer.SocketBound += HttpServer_SocketBound;
			httpServer.Start();

			thrSyncWithOtherServer = new Thread(SyncWithOtherServer);
			thrSyncWithOtherServer.IsBackground = true;
			thrSyncWithOtherServer.Name = "Sync with other server";
			thrSyncWithOtherServer.Start();
		}

		/// <summary>
		/// Stops the service.
		/// </summary>
		public void Stop()
		{
			httpServer?.Stop();
			httpServer = null;
			thrSyncWithOtherServer?.Abort();
			thrSyncWithOtherServer = null;
			Logger.StopLoggingThreads();
		}

		private void HttpServer_SocketBound(object sender, string e)
		{
			http_port = httpServer.Port_http;
			https_port = httpServer.Port_https;
			SocketBound(sender, e);
		}

		private void SyncWithOtherServer()
		{
			try
			{
				WebRequestUtility wru = new WebRequestUtility("MonitorControl " + Globals.AssemblyVersion, 1000);
				string lastStatus = "";
				int connectionFailureCount = 0;
				while (true)
				{
					try
					{
						if (!Program.settings.syncAllowLocalOverride)
							lastStatus = MonitorControlServer.currentMonitorStatus;
						string address = Program.settings.syncAddress;
						if (!string.IsNullOrWhiteSpace(address))
						{
							string url = "http" + (Program.settings.syncHTTPS ? "s" : "") + "://" + address + ":" + Program.settings.syncPort + "/status";
							BpWebResponse response = wru.GET(url);
							if (response.StatusCode == 0)
							{
								connectionFailureCount++;
								syncedServerStatus = "The remote server is not responding.  It may be misconfigured, blocked by a firewall, or not running.";
								if (connectionFailureCount >= 5) // Don't take action due to a short-term failure.
								{
									if (Program.settings.syncFailureAction == 1)
									{
										if (lastStatus != "off")
										{
											lastStatus = "off";
											MonitorControlServer.Off(Program.settings.syncMute);
										}
									}
									else if (Program.settings.syncFailureAction == 2)
									{
										if (lastStatus != "on")
										{
											lastStatus = "on";
											MonitorControlServer.On(null);
										}
									}
								}
							}
							else
							{
								connectionFailureCount = 0;
								if (response.StatusCode == 403)
								{
									syncedServerStatus = "The remote server rejected our request (this server's IP is probably not authorized there).";
								}
								else if (response.StatusCode != 200)
								{
									syncedServerStatus = "The remote server responded with unexpected status code " + response.StatusCode;
									try
									{
										syncedServerStatus += " " + response.str;
									}
									catch { }
								}
								else
								{
									try
									{
										syncedServerStatus = "The remote server monitors are " + response.str;
										if (response.str != lastStatus)
										{
											if (response.str == "off")
											{
												lastStatus = "off";
												MonitorControlServer.Off(Program.settings.syncMute);
											}
											else if (response.str == "on")
											{
												lastStatus = "on";
												MonitorControlServer.On(null);
											}
										}
									}
									catch
									{
										syncedServerStatus = "The remote server provided an invalid response.";
									}
								}
							}
						}
						else
						{
							connectionFailureCount = 0;
							syncedServerStatus = "Not configured to synchronize with a remote server.";
						}
						Thread.Sleep(1000);
					}
					catch (ThreadAbortException)
					{
					}
					catch (Exception ex)
					{
						syncedServerStatus = "An error occurred when synchronizing with a remote server. " + ex.ToString();
						Logger.Debug(ex);
					}
				}
			}
			catch (ThreadAbortException)
			{
			}
			catch (Exception ex)
			{
				syncedServerStatus = "Remote server sync has experienced a fatal error and will not resume operation until MonitorControl is restarted. " + ex.ToString();
				Logger.Debug(ex);
			}
		}
	}
}
