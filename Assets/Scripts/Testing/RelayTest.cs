using jKnepel.SimpleUnityNetworking.Managing;
using jKnepel.SimpleUnityNetworking.Networking;
using jKnepel.SimpleUnityNetworking.Networking.Transporting;
using jKnepel.SimpleUnityNetworking.SyncDataTypes;
using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class RelayTest : MonoBehaviour
{
    [SerializeField] private MonoNetworkManager _manager;
    [SerializeField] private uint _targetClientID;
    [SerializeField] private string _message;
    
    public bool IsOnline => _manager?.IsOnline ?? false;
    public bool IsServer => _manager?.IsServer ?? false;
    public bool IsClient => _manager?.IsClient ?? false;
    public bool IsHost => _manager?.IsHost ?? false;
    public string PlayerID { get; private set; }
    public string[] AllocationRegions { get; private set; }
    public string JoinCode { get; private set; }

    private void Start()
    {
        _ = InitializeNetwork();
    }
    
    public async Task InitializeNetwork()
    {
        await UnityServices.InitializeAsync();
        await AuthenticationService.Instance.SignInAnonymouslyAsync();
        if (!AuthenticationService.Instance.IsSignedIn)
        {
            Debug.LogError("An Error occurred trying to sign in the Player. Try again Later!");
            return;
        }

        PlayerID = AuthenticationService.Instance.PlayerId;
        AllocationRegions = (await RelayService.Instance.ListRegionsAsync()).Select(x => x.Id).ToArray();

        Debug.Log("The Player was Signed in!");
    }

    public async void StartServer(int maxPlayers, string allocationRegion)
    {
        if (_manager.Transport == null) return;
        
        var hostAllocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers, allocationRegion);
        JoinCode = await RelayService.Instance.GetJoinCodeAsync(hostAllocation.AllocationId);
        ((UnityTransport)_manager.Transport).SetRelayServerData(new(hostAllocation, "dtls"));
        _manager.StartServer("server");
    }

    public void StopServer()
    {
        _manager.StopServer();
    }

    public async void StartClient(string joinCode)
    {
        if (_manager.Transport == null) return;
        
        if (IsServer)
        {
            _manager.StartClient("user", new());
            return;
        }
        
        var joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
        ((UnityTransport)_manager.Transport).SetRelayServerData(new(joinAllocation, "udp"));
        _manager.StartClient("user", new());
    }

    public void StopClient()
    {
        _manager.StopClient();
    }

    public void Register()
    {
        _manager.RegisterStructData<MessageStruct>(ReceiveStruct);
    }

    public void Unregister()
    {
        _manager.UnregisterStructData<MessageStruct>(ReceiveStruct);
    }

    public void SendToClient(ENetworkChannel channel = ENetworkChannel.ReliableOrdered)
    {
        MessageStruct message = new()
        {
            String = _message,
            Byte = 1,
            Short = -2,
            UShort = 5,
            Int = -998,
            UInt = 213,
            Long = -12313123,
            ULong = 123123,
            Ints = new [] { 1, 2, 3 }
        };
        _manager.SendStructDataToClient(_targetClientID, message, channel);
    }

    private void ReceiveStruct(uint clientID, MessageStruct message)
    {
        Debug.Log($"Received from {clientID}: " +
                  $"String = {message.String},\n" +
                  $"Byte = {message.Byte},\n" +
                  $"Short = {message.Short},\n" +
                  $"UShort = {message.UShort},\n" +
                  $"Int = {message.Int},\n" +
                  $"UInt = {message.UInt},\n" +
                  $"Long = {message.Long},\n" +
                  $"ULong = {message.ULong}\n" +
                  $"Ints = {string.Join(",", message.Ints)},\n");
    }

    private struct MessageStruct : IStructData
    {
        public string String;
        public byte Byte;
        public short Short;
        public ushort UShort;
        public int Int;
        public uint UInt;
        public long Long;
        public ulong ULong;
        public int[] Ints;
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(RelayTest))]
public class RelayTestEditor : Editor
{
    private ENetworkChannel _channel = ENetworkChannel.ReliableOrdered;
    private int _maxNumberPlayers = 5;
    private int _allocationRegionIdx = 0;
    private string _joinCode = string.Empty;
    
	public override void OnInspectorGUI()
	{
        var test = (RelayTest)target;

        EditorGUILayout.PropertyField(serializedObject.FindProperty("_manager"));
        
        EditorGUILayout.Space();
        
        GUILayout.Label("Values:", EditorStyles.boldLabel);
        GUILayout.Label($"IsOnline: {test.IsOnline}");
        GUILayout.Label($"IsServer: {test.IsServer}");
        GUILayout.Label($"IsClient: {test.IsClient}");
        GUILayout.Label($"PlayerID: {test.PlayerID}");
        EditorGUILayout.TextField("JoinCode:", test.JoinCode);
        
        EditorGUILayout.Space();

        GUILayout.Label("Server:", EditorStyles.boldLabel);
        _maxNumberPlayers = EditorGUILayout.IntField("Max Players:", _maxNumberPlayers);
        _allocationRegionIdx = EditorGUILayout.Popup("Allocation Region:", _allocationRegionIdx, test.AllocationRegions ?? Array.Empty<string>());
        if (!test.IsServer && GUILayout.Button("Start Server"))
            test.StartServer(_maxNumberPlayers, test.AllocationRegions[_allocationRegionIdx]);
        if (test.IsServer && GUILayout.Button("Stop Server"))
            test.StopServer();
        
        EditorGUILayout.Space();

        GUILayout.Label("Client:", EditorStyles.boldLabel);
        _joinCode = EditorGUILayout.TextField("Join Code:", _joinCode);
        if (!test.IsClient && GUILayout.Button("Start Client"))
            test.StartClient(_joinCode);
        if (test.IsClient && GUILayout.Button("Stop Client"))
            test.StopClient();
        
        EditorGUILayout.Space();
        
        GUILayout.Label("Data:", EditorStyles.boldLabel);
        if (GUILayout.Button("Register"))
            test.Register();
        if (GUILayout.Button("Unregister"))
            test.Unregister();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("_targetClientID"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("_message"));
        _channel = (ENetworkChannel)EditorGUILayout.EnumPopup(_channel);
        if (GUILayout.Button("Send Message"))
            test.SendToClient(_channel);

        serializedObject.ApplyModifiedProperties();
    }
}
#endif