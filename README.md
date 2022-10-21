# portfolio-gameserver-csharp
C# 게임서버 포트폴리오

# 소개
C# 게임서버 포트폴리오입니다.


게임룸에 입장한 모든 플레이어들의 이동 및 전투를 동기화 하는 게임서버입니다. 

# 기능
:heavy_check_mark: 서버 설정(Config) 관리


:heavy_check_mark: 세션 관리


:heavy_check_mark: 패킷 처리


:heavy_check_mark: 데이터 관리


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

		object _lock = new object();
		int _sessionId = 0;
		
		//접속된 모든 클라이언트 세션 관리
		Dictionary<int, ClientSession> _sessions = new Dictionary<int, ClientSession>();

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
### **PacketManager.cs**
- 초기화 시 Register()를 호출하여 패킷 수신 시 처리해야 할 핸들러를 등록한다.
``` c#
class PacketManager
{
	#region Singleton
	static PacketManager _instance = new PacketManager();
	public static PacketManager Instance { get { return _instance; } }
	#endregion

	PacketManager()
	{
		Register();
	}

	Dictionary<ushort, Action<PacketSession, ArraySegment<byte>, ushort>> _onRecv = new Dictionary<ushort, Action<PacketSession, ArraySegment<byte>, ushort>>();
	Dictionary<ushort, Action<PacketSession, IMessage>> _handler = new Dictionary<ushort, Action<PacketSession, IMessage>>();
		
	public Action<PacketSession, IMessage, ushort> CustomHandler { get; set; }

	//패킷 수신 시 처리해야 할 핸들러를 등록 
	public void Register()
	{		
		_onRecv.Add((ushort)MsgId.CMove, MakePacket<C_Move>);
		_handler.Add((ushort)MsgId.CMove, PacketHandler.C_MoveHandler);		
		_onRecv.Add((ushort)MsgId.CSkill, MakePacket<C_Skill>);
		_handler.Add((ushort)MsgId.CSkill, PacketHandler.C_SkillHandler);
	}
}	
```
- 패킷을 수신하면 특정 패킷에 맞는 핸들러를 찾아 실행한다. 
``` c#
public void OnRecvPacket(PacketSession session, ArraySegment<byte> buffer)
{
	ushort count = 0;

	ushort size = BitConverter.ToUInt16(buffer.Array, buffer.Offset);
	count += 2;
	ushort id = BitConverter.ToUInt16(buffer.Array, buffer.Offset + count);
	count += 2;

	Action<PacketSession, ArraySegment<byte>, ushort> action = null;
	if (_onRecv.TryGetValue(id, out action))
		action.Invoke(session, buffer, id);
}
```


### **PacketHandler.cs**
- 각 컨텐츠 관련 패킷에 대한 실질적인 처리가 이루어지는 클래스이다.
- C_MoveHandler는 플레이어 이동 패킷을 처리한다.
- C_SkillHandler는 플레이어 스킬 발동 패킷을 처리한다.
``` c#
class PacketHandler
{
	//플레이어 패킷 이동 패킷 처리 핸들러
	public static void C_MoveHandler(PacketSession session, IMessage packet)
	{
		C_Move movePacket = packet as C_Move;
		ClientSession clientSession = session as ClientSession;

		//Console.WriteLine($"C_Move ({movePacket.PosInfo.PosX}, {movePacket.PosInfo.PosY})");

		Player player = clientSession.MyPlayer;
		if (player == null)
			return;

		GameRoom room = player.Room;
		if (room == null)
			return;

		room.Push(room.HandleMove, player, movePacket);
	}

    //플레이어 스킬 발동 패킷 처리 핸들러
    public static void C_SkillHandler(PacketSession session, IMessage packet)
	{
		C_Skill skillPacket = packet as C_Skill;
		ClientSession clientSession = session as ClientSession;

		Player player = clientSession.MyPlayer;
		if (player == null)
			return;

		GameRoom room = player.Room;
		if (room == null)
			return;

		room.Push(room.HandleSkill, player, skillPacket);
	}
}
```


### **JobSerializer.cs**
- 패킷 핸들러 처리는 서버 lock을 최소화 하기 위해 Command 패턴을 사용한다.

``` c#
namespace Server.Game
{
	//패킷 핸들러 처리는 서버 lock을 최소화 하기 위해 JobQueue 방식을 사용한다.
	public class JobSerializer
	{
		JobTimer _timer = new JobTimer();
		Queue<IJob> _jobQueue = new Queue<IJob>();
		object _lock = new object();
		bool _flush = false;

		//...이하 생략
	}
}
```
- 핸들러를 Job으로 변환하여 _jobQueue 넣어준다.
``` c#
public void Push(IJob job)
{
	lock (_lock)
	{
		_jobQueue.Enqueue(job);
	}
}
```
- 게임룸에서 특정 시간 주기로 Tick이 발동되며 Flush를 호출한다.
- Flush()에서 _jobQueue에 쌓여있는 것들을 차례로 실행한다.
``` c#		
public void Flush()
{
	_timer.Flush();

	while (true)
	{
		IJob job = Pop();
		if (job == null)
			return;

		job.Execute();
	}
}
```


## 게임 데이터 관리
### **DataManager.cs**
- 게임에 필요한 데이터 Dictionary 형태로 관리한다.
- 게임 시작 시 json 파일을 읽어와 dictionary에 저장한다.
``` c#
namespace Server.Data
{
	public interface ILoader<Key, Value>
	{
		Dictionary<Key, Value> MakeDict();
	}

	public class DataManager
	{
		//데이터는 Dictionary 형태로 관리
		public static Dictionary<int, StatInfo> StatDict { get; private set; } = new Dictionary<int, StatInfo>();
		public static Dictionary<int, Data.Skill> SkillDict { get; private set; } = new Dictionary<int, Data.Skill>();

		public static void LoadData()
		{
			StatDict = LoadJson<Data.StatData, int, StatInfo>("StatData").MakeDict();
			SkillDict = LoadJson<Data.SkillData, int, Data.Skill>("SkillData").MakeDict();
		}

		static Loader LoadJson<Loader, Key, Value>(string path) where Loader : ILoader<Key, Value>
		{
			string text = File.ReadAllText($"{ConfigManager.Config.dataPath}/{path}.json");
			return Newtonsoft.Json.JsonConvert.DeserializeObject<Loader>(text);
		}
	}
}
```
## 게임룸 입장 및 관리
## 플레이어 이동 동기화
## 플레이어 스킬 처리
## 플레이어 Hit 판정 처리
## 플레이어 Damage 처리
## 적->플레이어 Search AI
## 적->플레이어 Skill AI
