// MultiTeamNetworkManager.cs
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Threading;

public class MultiTeamNetworkManager : MonoBehaviour
{
    [Header("General Settings")]
    [Tooltip("If true, this instance acts as the server; otherwise, it acts as a client.")]
    public bool isServer = false;
    [Tooltip("TCP port number to use for communication.")]
    public int port = 7777;
    [Tooltip("IP address of the server (client mode only).")]
    public string serverIP = "127.0.0.1";

    // A thread–safe queue to pass network messages to Unity's main thread.
    private Queue<string> messageQueue = new Queue<string>();
    private readonly object queueLock = new object();

    #region Server Variables

    // Class to hold information about a connected client.
    private class ClientInfo
    {
        public TcpClient client;
        public string team;
        public StreamReader reader;
        public StreamWriter writer;
    }

    // List of connected clients (used on the server).
    private List<ClientInfo> connectedClients = new List<ClientInfo>();

    private TcpListener tcpListener;
    private Thread listenerThread;

    #endregion

    #region Client Variables

    private TcpClient tcpClient;
    private Thread clientThread;
    private StreamReader clientReader;
    private StreamWriter clientWriter;

    // For client instances, the team is set via GameSettings.
    private string clientTeam;

    #endregion

    // An optional event to subscribe to incoming messages.
    public delegate void MessageReceivedHandler(string message);
    public event MessageReceivedHandler OnMessageReceived;

    void Start()
    {
        // Determine role based on GameSettings.
        isServer = GameSettings.IsServer;
        if (!isServer)
        {
            clientTeam = GameSettings.Team;
        }

        // Start in server or client mode.
        if (isServer)
        {
            StartServer();
        }
        else
        {
            StartClient();
        }
    }

    #region Server Methods

    public void StartServer()
    {
        try
        {
            tcpListener = new TcpListener(IPAddress.Any, port);
            tcpListener.Start();
            Debug.Log("[Server] Started on port " + port);

            // Start a background thread to accept clients.
            listenerThread = new Thread(ListenForClients)
            {
                IsBackground = true
            };
            listenerThread.Start();
        }
        catch (Exception ex)
        {
            Debug.LogError("[Server] Error starting: " + ex.Message);
        }
    }

    private void ListenForClients()
    {
        while (true)
        {
            try
            {
                // Block until a client connects.
                TcpClient client = tcpListener.AcceptTcpClient();
                ClientInfo clientInfo = new ClientInfo();
                clientInfo.client = client;

                NetworkStream stream = client.GetStream();
                clientInfo.reader = new StreamReader(stream);
                clientInfo.writer = new StreamWriter(stream) { AutoFlush = true };

                lock (connectedClients)
                {
                    connectedClients.Add(clientInfo);
                }

                Debug.Log("[Server] Client connected: " + client.Client.RemoteEndPoint.ToString());

                // Start a thread to handle messages from this client.
                Thread clientThread = new Thread(() => HandleClientComm(clientInfo))
                {
                    IsBackground = true
                };
                clientThread.Start();
            }
            catch (Exception ex)
            {
                Debug.LogError("[Server] Listener exception: " + ex.Message);
                break;
            }
        }
    }

    private void HandleClientComm(ClientInfo clientInfo)
    {
        try
        {
            // First, expect a join message from the client in the format "JOIN:TeamName"
            string joinMessage = clientInfo.reader.ReadLine();
            if (joinMessage != null && joinMessage.StartsWith("JOIN:"))
            {
                clientInfo.team = joinMessage.Substring("JOIN:".Length).Trim();
                Debug.Log("[Server] Client joined as team: " + clientInfo.team);
                EnqueueMessage($"[Server] Client from {clientInfo.client.Client.RemoteEndPoint} joined as {clientInfo.team}");
            }
            else
            {
                Debug.LogWarning("[Server] Client did not send proper join message.");
                clientInfo.team = "Unknown";
            }

            // Now, keep listening for action messages.
            while (clientInfo.client.Connected)
            {
                string message = clientInfo.reader.ReadLine();
                if (message != null)
                {
                    Debug.Log($"[Server] Received from {clientInfo.team}: {message}");
                    // Broadcast this action message to all other clients.
                    BroadcastMessage($"[{clientInfo.team}] {message}", clientInfo);
                }
                else
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("[Server] Client communication error: " + ex.Message);
        }
        finally
        {
            // Remove the client when done.
            lock (connectedClients)
            {
                connectedClients.Remove(clientInfo);
            }
            clientInfo.client.Close();
            Debug.Log("[Server] Client disconnected.");
        }
    }

    /// <summary>
    /// Broadcasts a message to all connected clients except the sender.
    /// </summary>
    /// <param name="message">The message to send.</param>
    /// <param name="sender">The originating client (can be null).</param>
    private void BroadcastMessage(string message, ClientInfo sender)
    {
        lock (connectedClients)
        {
            foreach (ClientInfo client in connectedClients)
            {
                if (client != sender)
                {
                    try
                    {
                        client.writer.WriteLine(message);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError("[Server] Error sending to client: " + ex.Message);
                    }
                }
            }
        }
        // Also queue the message on the server for display/logging.
        EnqueueMessage(message);
    }

    #endregion

    #region Client Methods

    public void StartClient()
    {
        try
        {
            tcpClient = new TcpClient();
            tcpClient.Connect(serverIP, port);
            Debug.Log("[Client] Connected to server at " + serverIP + ":" + port);

            NetworkStream stream = tcpClient.GetStream();
            clientReader = new StreamReader(stream);
            clientWriter = new StreamWriter(stream) { AutoFlush = true };

            // Send the join message (e.g., "JOIN:Red" or "JOIN:Blue")
            clientWriter.WriteLine("JOIN:" + clientTeam);
            Debug.Log("[Client] Sent join message with team: " + clientTeam);

            // Start a background thread to receive messages.
            clientThread = new Thread(ClientReceiveLoop)
            {
                IsBackground = true
            };
            clientThread.Start();
        }
        catch (Exception ex)
        {
            Debug.LogError("[Client] Connection error: " + ex.Message);
        }
    }

    private void ClientReceiveLoop()
    {
        try
        {
            while (tcpClient.Connected)
            {
                string message = clientReader.ReadLine();
                if (message != null)
                {
                    Debug.Log("[Client] Received: " + message);
                    EnqueueMessage(message);
                }
                else
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("[Client] Receive error: " + ex.Message);
        }
    }

    #endregion

    #region Message Queue and Main-Thread Processing

    private void EnqueueMessage(string message)
    {
        lock (queueLock)
        {
            messageQueue.Enqueue(message);
        }
    }

    private void ProcessMessageQueue()
    {
        lock (queueLock)
        {
            while (messageQueue.Count > 0)
            {
                string msg = messageQueue.Dequeue();
                Debug.Log("Message: " + msg);
                OnMessageReceived?.Invoke(msg);
            }
        }
    }

    void Update()
    {
        ProcessMessageQueue();
    }

    #endregion

    #region Sending Action Messages

    /// <summary>
    /// Call this method from your game logic (for example, when a player performs an action)
    /// to send an action message to the server.
    /// </summary>
    /// <param name="actionMessage">A string representing the action.</param>
    public void SendActionMessage(string actionMessage)
    {
        if (isServer)
        {
            // If the server itself needs to send a message.
            BroadcastMessage("[Server] " + actionMessage, null);
        }
        else
        {
            try
            {
                if (tcpClient != null && tcpClient.Connected)
                {
                    clientWriter.WriteLine(actionMessage);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[Client] Error sending action message: " + ex.Message);
            }
        }
    }

    #endregion

    #region Shutdown and Cleanup

    void OnApplicationQuit()
    {
        Shutdown();
    }

    void OnDestroy()
    {
        Shutdown();
    }

    private void Shutdown()
    {
        try
        {
            if (isServer)
            {
                if (tcpListener != null)
                {
                    tcpListener.Stop();
                }
                lock (connectedClients)
                {
                    foreach (ClientInfo client in connectedClients)
                    {
                        client.client.Close();
                    }
                    connectedClients.Clear();
                }
                if (listenerThread != null && listenerThread.IsAlive)
                    listenerThread.Abort();
            }
            else
            {
                if (tcpClient != null)
                {
                    tcpClient.Close();
                }
                if (clientThread != null && clientThread.IsAlive)
                    clientThread.Abort();
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("Shutdown error: " + ex.Message);
        }
    }

    #endregion
}
