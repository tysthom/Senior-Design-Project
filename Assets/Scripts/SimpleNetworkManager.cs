using UnityEngine;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Threading;

public class SimpleNetworkManager : MonoBehaviour
{
    [Header("General Settings")]
    [Tooltip("If true, this instance acts as the server; otherwise, it acts as the client.")]
    public bool isServer = true;
    [Tooltip("Automatically start the connection on Awake.")]
    public bool autoStart = true;
    [Tooltip("TCP port number.")]
    public int port = 7777;

    [Header("Client Settings")]
    [Tooltip("IP address of the server to connect to (client mode only).")]
    public string serverIP = "127.0.0.1";

    // A thread–safe queue to pass messages from networking threads to the Unity main thread.
    private Queue<string> messageQueue = new Queue<string>();
    private readonly object queueLock = new object();

    // Server–side variables
    private TcpListener tcpListener;
    private Thread listenerThread;
    private readonly List<TcpClient> connectedClients = new List<TcpClient>();
    private readonly List<Thread> clientThreads = new List<Thread>();

    // Client–side variables
    private TcpClient tcpClient;
    private Thread clientThread;
    private StreamReader clientReader;
    private StreamWriter clientWriter;

    // An event to handle received messages – you can subscribe to this event in other scripts.
    public delegate void MessageReceivedHandler(string message);
    public event MessageReceivedHandler OnMessageReceived;

    void Start()
    {
        if (autoStart)
        {
            if (isServer)
                StartServer();
            else
                StartClient();
        }
    }

    #region Server Methods

    /// <summary>
    /// Initializes and starts the server.
    /// </summary>
    public void StartServer()
    {
        try
        {
            tcpListener = new TcpListener(IPAddress.Any, port);
            tcpListener.Start();
            Debug.Log($"[Server] Started on port {port}.");

            // Start a background thread that accepts incoming client connections.
            listenerThread = new Thread(ListenForClients)
            {
                IsBackground = true
            };
            listenerThread.Start();
        }
        catch (Exception ex)
        {
            Debug.LogError("[Server] Start error: " + ex.Message);
        }
    }

    /// <summary>
    /// Continuously listens for client connections.
    /// </summary>
    private void ListenForClients()
    {
        while (true)
        {
            try
            {
                // Blocks until a client connects.
                TcpClient client = tcpListener.AcceptTcpClient();
                lock (connectedClients)
                {
                    connectedClients.Add(client);
                }
                Debug.Log("[Server] Client connected: " + client.Client.RemoteEndPoint.ToString());

                // Start a new thread to handle communication with this client.
                Thread clientThread = new Thread(() => HandleClientComm(client))
                {
                    IsBackground = true
                };
                clientThread.Start();
                lock (clientThreads)
                {
                    clientThreads.Add(clientThread);
                }
            }
            catch (Exception ex)
            {
                Debug.Log("[Server] Listener thread exception: " + ex.Message);
                break;
            }
        }
    }

    /// <summary>
    /// Handles the communication with an individual client.
    /// </summary>
    /// <param name="client">The connected TcpClient.</param>
    private void HandleClientComm(TcpClient client)
    {
        try
        {
            NetworkStream clientStream = client.GetStream();
            StreamReader reader = new StreamReader(clientStream);
            // (Optional) Create a StreamWriter if you wish to reply immediately.
            StreamWriter writer = new StreamWriter(clientStream) { AutoFlush = true };

            while (client.Connected)
            {
                // ReadLine blocks until a message is received (messages are assumed to be terminated by newline).
                string message = reader.ReadLine();
                if (message != null)
                {
                    // Enqueue the message for processing on the main thread.
                    EnqueueMessage("[Client] " + message);

                    // (Optional) Respond to the client.
                    // writer.WriteLine("Server received: " + message);
                }
                else
                {
                    // The client has closed the connection.
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.Log("[Server] Client communication error: " + ex.Message);
        }
        finally
        {
            // Remove the client from the list on disconnect.
            lock (connectedClients)
            {
                connectedClients.Remove(client);
            }
            client.Close();
        }
    }

    #endregion

    #region Client Methods

    /// <summary>
    /// Connects to a server as a client.
    /// </summary>
    public void StartClient()
    {
        try
        {
            tcpClient = new TcpClient();
            tcpClient.Connect(serverIP, port);
            Debug.Log($"[Client] Connected to server at {serverIP}:{port}");

            NetworkStream stream = tcpClient.GetStream();
            clientReader = new StreamReader(stream);
            clientWriter = new StreamWriter(stream) { AutoFlush = true };

            // Start a background thread to listen for messages from the server.
            clientThread = new Thread(ClientReceiveLoop)
            {
                IsBackground = true
            };
            clientThread.Start();
        }
        catch (Exception ex)
        {
            Debug.LogError("[Client] Start error: " + ex.Message);
        }
    }

    /// <summary>
    /// Continuously listens for messages from the server.
    /// </summary>
    private void ClientReceiveLoop()
    {
        try
        {
            while (tcpClient.Connected)
            {
                string message = clientReader.ReadLine();
                if (message != null)
                {
                    EnqueueMessage("[Server] " + message);
                }
                else
                {
                    // The server closed the connection.
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.Log("[Client] Receive error: " + ex.Message);
        }
    }

    #endregion

    #region Message Queue and Sending

    /// <summary>
    /// Enqueues a received message to be processed on the Unity main thread.
    /// </summary>
    /// <param name="message">The message received from the network.</param>
    private void EnqueueMessage(string message)
    {
        lock (queueLock)
        {
            messageQueue.Enqueue(message);
        }
    }

    /// <summary>
    /// Processes and dispatches all messages from the queue.
    /// </summary>
    private void ProcessMessageQueue()
    {
        lock (queueLock)
        {
            while (messageQueue.Count > 0)
            {
                string msg = messageQueue.Dequeue();
                Debug.Log("Received: " + msg);

                // Invoke any subscribed message–received event handlers.
                OnMessageReceived?.Invoke(msg);
            }
        }
    }

    /// <summary>
    /// Sends a message. In server mode it sends to all connected clients; in client mode it sends to the server.
    /// </summary>
    /// <param name="message">The text message to send.</param>
    public void SendMessage(string message)
    {
        if (isServer)
        {
            // Server: send to every connected client.
            lock (connectedClients)
            {
                foreach (var client in connectedClients)
                {
                    try
                    {
                        if (client.Connected)
                        {
                            // Creating a new StreamWriter here is simple but not optimal for high–performance applications.
                            StreamWriter writer = new StreamWriter(client.GetStream()) { AutoFlush = true };
                            writer.WriteLine(message);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.Log("[Server] Error sending message to client: " + ex.Message);
                    }
                }
            }
        }
        else
        {
            // Client: send to the server.
            try
            {
                if (tcpClient != null && tcpClient.Connected)
                {
                    clientWriter.WriteLine(message);
                }
            }
            catch (Exception ex)
            {
                Debug.Log("[Client] Error sending message: " + ex.Message);
            }
        }
    }

    #endregion

    void Update()
    {
        // Process any messages received from the network.
        ProcessMessageQueue();
    }

    #region Shutdown and Cleanup

    void OnApplicationQuit()
    {
        Shutdown();
    }

    void OnDestroy()
    {
        Shutdown();
    }

    /// <summary>
    /// Closes all open network connections and stops background threads.
    /// </summary>
    private void Shutdown()
    {
        try
        {
            if (isServer)
            {
                // Stop the listener.
                if (tcpListener != null)
                {
                    tcpListener.Stop();
                }
                // Close all client connections.
                lock (connectedClients)
                {
                    foreach (var client in connectedClients)
                    {
                        client.Close();
                    }
                    connectedClients.Clear();
                }
                // Abort listener and client threads.
                if (listenerThread != null && listenerThread.IsAlive)
                    listenerThread.Abort();
                lock (clientThreads)
                {
                    foreach (var thread in clientThreads)
                    {
                        if (thread.IsAlive)
                            thread.Abort();
                    }
                    clientThreads.Clear();
                }
            }
            else
            {
                // Client shutdown.
                if (tcpClient != null)
                {
                    tcpClient.Close();
                }
                if (clientThread != null && clientThread.IsAlive)
                {
                    clientThread.Abort();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.Log("Shutdown error: " + ex.Message);
        }
    }

    #endregion

    #region Expansion Placeholders

    // You can add additional helper methods, message parsing logic,
    // or even switch to a more sophisticated protocol here.
    // For example, you might implement:
    // - A custom message format (e.g., JSON or binary).
    // - Handling for different message types (chat, movement, etc.).
    // - Encryption or authentication.
    // - Multiple client management with unique identifiers.

    #endregion
}
