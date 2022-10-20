# portfolio-gameserver-csharp
C# 게임서버 포트폴리오

## 소개
C# 게임서버 포트폴리오입니다.

## 기능
:heavy_check_mark: 서버 설정(Config) 관리


:heavy_check_mark: 세션 관리


:heavy_check_mark: 패킷 처리


:heavy_check_mark: 데이터 관리


:heavy_check_mark: Job 관리


:heavy_check_mark: 게임룸 입장 및 관리


:heavy_check_mark: 플레이어 이동 동기화


:heavy_check_mark: 플레이어 스킬 처리


:heavy_check_mark: 플레이어 Hit 판정 처리


:heavy_check_mark: 플레이어 Damage 처리


:heavy_check_mark: 적->플레이어 Search AI


:heavy_check_mark: 적->플레이어 Skill AI

## 게임 설정(Config) 관리
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
## 세션 관리
**SessionManager.cs**
- 접속된 클라이언트 세션 정보를 저장 및 관리한다.
- 클라이언트가 서버에 접속하연 전용 ClientSession 객체를 생성하고 SessionId를 부여한다.
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
**ClientSession.cs**
- ClientSession 클래스는 SessionId와 해당 플레이어의 정보를 갖고 있다.
``` c#
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
``` c#
- 서버에 접속/접속해제 시 처리가 정의되어 있다.
``` c#
public override void OnConnected(EndPoint endPoint)
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
- 클라로부터 패킷을 받았을 때/보냈을 때 처리가 정의되어 있다. 
``` c#
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
## 패킷 처리
## 게임 데이터 관리
## Job 관리
## 게임룸 입장 및 관리
## 플레이어 이동 동기화
## 플레이어 스킬 처리
## 플레이어 Hit 판정 처리
## 플레이어 Damage 처리
## 적->플레이어 Search AI
## 적->플레이어 Skill AI
