# portfolio-gameserver-csharp
C# 게임서버 포트폴리오

## 소개
C# 게임서버 포트폴리오입니다.

## 기능
:heavy_check_mark: ConfigManager


:heavy_check_mark: SessionManager


:heavy_check_mark: PacketManager


:heavy_check_mark: DataManager


:heavy_check_mark: RoomManager


:heavy_check_mark: JobManager


## ConfigManager
``` c#
namespace Server.Data
{
	//설정과 관련된 정보를 저장하는 클래스
	[Serializable]
	public class ServerConfig
	{
		public string dataPath;
		public string connectionIP;
		public string connectionPort;
	}

	public class ConfigManager
	{
		public static ServerConfig Config { get; private set; }

		//config.json파일을 로드하여 Config 객체에 매핑해 준다.
		public static void LoadConfig()
		{
			string text = File.ReadAllText("config.json");
			Config = Newtonsoft.Json.JsonConvert.DeserializeObject<ServerConfig>(text);
		}
	}
}
```
## SessionManager
- SessionManager.cs


접속된 클라이언트 세션 정보를 저장 및 관리한다. 클라이언트가 서버에 접속하연 전용 ClientSession 객체를 생성하고 SessionId를 부여한다.
``` c#
namespace Server
{
	class SessionManager
	{
		static SessionManager _session = new SessionManager();
		public static SessionManager Instance { get { return _session; } }

		int _sessionId = 0;
		Dictionary<int, ClientSession> _sessions = new Dictionary<int, ClientSession>();
		object _lock = new object();

		public ClientSession Generate()
		{
			lock (_lock)
			{
				int sessionId = ++_sessionId;

				ClientSession session = new ClientSession();
				session.SessionId = sessionId;
				_sessions.Add(sessionId, session);

				Console.WriteLine($"Connected : {sessionId}");

				return session;
			}
		}
		
		//...이하 생략
	}
}
```
- ClientSession.cs


ClientSession 클래스는 SessionId와 해당 플레이어의 정보를 갖고 있다.
```
namespace Server
{	
	public class ClientSession : PacketSession
	{
		public Player MyPlayer { get; set; }
		public int SessionId { get; set; }
	
		...
	}
	//...이하 생략
}
```
서버에 접속/접속해제 시 처리가 정의되어 있다.
```
{
	Console.WriteLine($"OnConnected : {endPoint}");

	MyPlayer = ObjectManager.Instance.Add<Player>();
	{
		MyPlayer.Info.Name = $"Player_{MyPlayer.Info.ObjectId}";
		MyPlayer.Info.PosInfo.State = CreatureState.Idle;
		MyPlayer.Info.PosInfo.MoveDir = MoveDir.Down;
		MyPlayer.Info.PosInfo.PosX = 0;
		MyPlayer.Info.PosInfo.PosY = 0;

		StatInfo stat = null;
		DataManager.StatDict.TryGetValue(1, out stat);
		MyPlayer.Stat.MergeFrom(stat);

		MyPlayer.Session = this;
	}

	GameRoom room = RoomManager.Instance.Find(1);
	room.Push(room.EnterGame, MyPlayer);
}

public override void OnDisconnected(EndPoint endPoint)
{
	GameRoom room = RoomManager.Instance.Find(1);
	room.Push(room.LeaveGame, MyPlayer.Info.ObjectId);

	SessionManager.Instance.Remove(this);

	Console.WriteLine($"OnDisconnected : {endPoint}");
}
```
클라로부터 패킷을 받았을 때/보냈을 때 처리가 정의되어 있다. 
```
public void Send(IMessage packet)
{
	string msgName = packet.Descriptor.Name.Replace("_", string.Empty);
	MsgId msgId = (MsgId)Enum.Parse(typeof(MsgId), msgName);
	ushort size = (ushort)packet.CalculateSize();
	byte[] sendBuffer = new byte[size + 4];
	Array.Copy(BitConverter.GetBytes((ushort)(size + 4)), 0, sendBuffer, 0, sizeof(ushort)); //버퍼 사이즈 값 추가
	Array.Copy(BitConverter.GetBytes((ushort)msgId), 0, sendBuffer, 2, sizeof(ushort)); //msgId 값 추가
	Array.Copy(packet.ToByteArray(), 0, sendBuffer, 4, size);
	
	Send(new ArraySegment<byte>(sendBuffer));
}

public override void OnRecvPacket(ArraySegment<byte> buffer)
{
	PacketManager.Instance.OnRecvPacket(this, buffer);
}
```
## PacketManager
## DataManager
## RoomManager
## JobManager
