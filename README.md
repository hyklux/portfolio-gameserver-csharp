# portfolio-gameserver-csharp
C# 게임서버 포트폴리오

# 소개
C# 게임서버 포트폴리오입니다.
게임룸에 입장한 모든 플레이어들의 이동을 동기화하고, 플레이어와 적간의 공격 및 Hit 판정을 처리합니다.
적 AI를 구현해 적이 스스로 플레이어(나)를 쫒아와 공격합니다. 

# 기능
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
- 설정과 관련된 정보를 Config.json로 작성한다.
- 서버가 시작되면 LoadConfig()를 호출하여 Config 파일을 로드 후, ServerConfig 객체에 매핑한다.
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
### **SessionManager.cs**
- 접속된 클라이언트 세션 정보를 저장 및 관리한다.
- 클라이언트가 서버에 접속하연 Generate()를 호출하여 전용 ClientSession 객체를 생성하고 SessionId를 부여한다.
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

		//클라이언트가 서버 접속 시 호출
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
### **ClientSession.cs**
- ClientSession 클래스는 SessionId와 해당 플레이어의 정보를 갖고 있다.
``` c#
namespace Server
{	
	public class ClientSession : PacketSession
	{
		public Player MyPlayer { get; set; }
		public int SessionId { get; set; }
	}
	
	//...이하 생략
}
```
- 서버에 접속/접속해제 시 처리가 정의되어 있다.
- 생성 직후 OnConnected()가 호출되어 나의 플레이어 정보가 생성되고 게임룸에 입장한다.
- 접속 해제 시 게임룸에서 퇴장시키며 ClientSession 객제도 더 이상 SessionManager에 의해 관리되지 않게 된다.
``` c#
//접속 시 호출
public override void OnConnected(EndPoint endPoint)
{
	Console.WriteLine($"OnConnected : {endPoint}");

	//나의 플레이어 정보 생성
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

	//게임룸 입장
	GameRoom room = RoomManager.Instance.Find(1);
	room.Push(room.EnterGame, MyPlayer);
}

//접속 해제 시 호출
public override void OnDisconnected(EndPoint endPoint)
{
	//게임룸 퇴장
	GameRoom room = RoomManager.Instance.Find(1);
	room.Push(room.LeaveGame, MyPlayer.Info.ObjectId);

	//세션매니저에서 해제
	SessionManager.Instance.Remove(this);

	Console.WriteLine($"OnDisconnected : {endPoint}");
}
```
- 클라로부터 패킷을 수신/송신 시 처리가 정의되어 있다. 
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
