﻿using System.Drawing;
using AssettoServer.Network.Packets.Outgoing;
using AssettoServer.Network.Tcp;
using AssettoServer.Server;
using Discord;
using Discord.Webhook;
using Serilog;

namespace DiscordAuditPlugin;

public class Discord
{
    private static readonly string[] SensitiveCharacters = { "\\", "*", "_", "~", "`", "|", ">", ":", "@" };
    private readonly ACServer _server;
    
    private DiscordWebhook AuditHook { get; }
    private DiscordWebhook ChatHook { get; }
    private DiscordConfiguration Configuration { get; }

    public Discord(ACServer server, DiscordConfiguration configuration)
    {
        _server = server;
        Configuration = configuration;
        
        if (!string.IsNullOrEmpty(Configuration.AuditUrl))
        {
            AuditHook = new DiscordWebhook
            {
                Url = Configuration.AuditUrl
            };

            server.ClientKicked += OnClientKicked;
            server.ClientBanned += OnClientBanned;
        }
        
        if (!string.IsNullOrEmpty(Configuration.ChatUrl))
        {
            ChatHook = new DiscordWebhook
            {
                Url = Configuration.ChatUrl
            };

            server.ChatMessageReceived += OnChatMessageReceived;
        }
    }

    private void OnClientBanned(ACTcpClient sender, ClientAuditEventArgs args)
    {
        Run(() =>
        {
            AuditHook.Send(PrepareAuditMessage(
                ":hammer: Ban alert",
                _server.Configuration.Name,
                sender.Guid,
                sender.Name,
                args.ReasonStr,
                Color.Red,
                args.Admin?.Name
            ));
        });
    }

    private void OnClientKicked(ACTcpClient sender, ClientAuditEventArgs args)
    {
        if (args.Reason != KickReason.ChecksumFailed)
        {
            Run(() =>
            {
                AuditHook.Send(PrepareAuditMessage(
                    ":boot: Kick alert",
                    _server.Configuration.Name,
                    sender.Guid,
                    sender.Name,
                    args.ReasonStr,
                    Color.Yellow,
                    args.Admin?.Name
                ));
            });
        }
    }

    private void OnChatMessageReceived(ACTcpClient sender, ChatEventArgs args)
    {
        if (!args.Message.StartsWith("\t\t\t\t$CSP0:"))
        {
            Run(() =>
            {
                DiscordMessage msg = new DiscordMessage
                {
                    AvatarUrl = Configuration.PictureUrl,
                    Username = sender.Name,
                    Content = Sanitize(args.Message)
                };

                ChatHook.Send(msg);
            });
        }
    }

    private static void Run(Action action)
    {
        Task.Run(action)
            .ContinueWith(t => Log.Error(t.Exception, "Error in Discord webhook"), TaskContinuationOptions.OnlyOnFaulted);
    }

    private DiscordMessage PrepareAuditMessage(
        string title,
        string serverName,
        string clientGuid,
        string clientName,
        string reason,
        Color color,
        string adminName
    )
    {
        string userSteamUrl = "https://steamcommunity.com/profiles/" + clientGuid;
        DiscordMessage message = new DiscordMessage
        {
            Username = serverName.Substring(0, Math.Min(serverName.Length, 80)),
            AvatarUrl = Configuration.PictureUrl,
            Embeds = new List<DiscordEmbed>
            {
                new()
                {
                    Title = title,
                    Color = color,
                    Fields = new List<EmbedField>
                    {
                        new() { Name = "Name", Value = Sanitize(clientName), InLine = true },
                        new() { Name = "Steam-GUID", Value = clientGuid + " ([link](" + userSteamUrl + "))", InLine = true }
                    }
                }
            }
        };

        if (adminName != null)
            message.Embeds[0].Fields.Add(new EmbedField { Name = "By Admin", Value = Sanitize(adminName), InLine = true });

        if (reason != null)
            message.Embeds[0].Fields.Add(new EmbedField { Name = "Message", Value = Sanitize(reason) });

        return message;
    }

    private static string Sanitize(string text)
    {
        foreach (string unsafeChar in SensitiveCharacters)
            text = text.Replace(unsafeChar, $"\\{unsafeChar}");
        return text;
    }
}