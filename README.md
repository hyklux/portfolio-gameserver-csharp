# portfolio-gameserver-csharp
2D action game server implemented using C#

# Introduction
This is 2D action game server implemented using C#


It is a game server that synchronizes the movement and battle of all players who have entered the game room.


# Implementations
:heavy_check_mark: Server configuration management


:heavy_check_mark: Session management


:heavy_check_mark: Packet processing


:heavy_check_mark: Data management


:heavy_check_mark: Game room management


:heavy_check_mark: Map


:heavy_check_mark: Player movement 


:heavy_check_mark: Player combat


:heavy_check_mark: NPC AI - Search player


:heavy_check_mark: NPC AI - Combat


# Server configuration management
- Write configuration-related information as Config.json.
- When the server starts, LoadConfig() is called to load the config file and map it to the ServerConfig object.
``` c#
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

	//Load config.json file and map it to Config object.
	public static void LoadConfig()
	{
		string text = File.ReadAllText("config.json");
		Config = Newtonsoft.Json.JsonConvert.DeserializeObject<ServerConfig>(text);
	}
}
```


# Session management
### **SessionManager.cs**
- Stores and manages connected client session information.
- When a client connects to the server, Generate() is called to create a dedicated ClientSession object and give it a SessionId.
``` c#
class SessionManager
{
	static SessionManager _session = new SessionManager();
	public static SessionManager Instance { get { return _session; } }

	object _lock = new object();
	int _sessionId = 0;
	
	//Stores all connected client sessions
	Dictionary<int, ClientSession> _sessions = new Dictionary<int, ClientSession>();

	//Called when client connects to server
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
		
	//...(omitted)
}
```
### **ClientSession.cs**
- The ClientSession class holds the SessionId and information about the player.
``` c#
public class ClientSession : PacketSession
{
	public Player MyPlayer { get; set; }
	public int SessionId { get; set; }

	//...(omitted)
}
```
- Processing is defined when connecting/disconnecting from the server.
- Right after creation, I call OnConnected(EndPoint endPoint) to create my player information and enter the game room.
- When disconnected, OnDisconnected(EndPoint endPoint) is called to exit the game room, and the ClientSession object is no longer managed by the SessionManager.
``` c#
public class ClientSession : PacketSession
{
	//...(omitted)
	
	//Called when connected
	public override void OnConnected(EndPoint endPoint)
	{
		Console.WriteLine($"OnConnected : {endPoint}");

		//Create my player info
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

		//Enter game room
		GameRoom room = RoomManager.Instance.Find(1);
		room.Push(room.EnterGame, MyPlayer);
	}
		
	//Called when disconnected
	public override void OnDisconnected(EndPoint endPoint)
	{
		//Exit game room
		GameRoom room = RoomManager.Instance.Find(1);
		room.Push(room.LeaveGame, MyPlayer.Info.ObjectId);

		//Removes from session manager
		SessionManager.Instance.Remove(this);

		Console.WriteLine($"OnDisconnected : {endPoint}");
	}
	
	//...(omitted)
}
```
- Processing is defined when receiving/transmitting packets from the release client in the session manager.
``` c#
public class ClientSession : PacketSession
{
	//...(omitted)
	
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
	
	//...(omitted)
}
```


# Packet processing
### **PacketManager.cs**
(캡처 필요)
- 초기화 시 Register()를 호출하여 패킷 수신 시 처리해야 할 핸들러를 등록합니다.
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
	
	//...(중략)
}	
```
- 패킷을 수신하면 특정 패킷에 맞는 핸들러를 찾아 실행합니다. 
- (추가 설명 필요) id, size 관련
``` c#
class PacketManager
{
	//...(중략)
	
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
	
	//...(중략)
}
```


### **PacketHandler.cs**
- 각 컨텐츠 관련 패킷에 대한 실질적인 처리가 이루어지는 클래스입니다.
- C_MoveHandler는 플레이어 이동 패킷을 처리합니다.
- C_SkillHandler는 플레이어 스킬 발동 패킷을 처리합니다.
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


# 게임 데이터 관리


### **DataManager.cs**
- 게임에 필요한 데이터 Dictionary 형태로 관리합니다.
- 게임 시작 시 json 파일을 읽어와 dictionary에 저장합니다.
``` c#
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
		//스탯데이터 로드
		StatDict = LoadJson<Data.StatData, int, StatInfo>("StatData").MakeDict();
		//스킬데이터 로드
		SkillDict = LoadJson<Data.SkillData, int, Data.Skill>("SkillData").MakeDict();
	}

	static Loader LoadJson<Loader, Key, Value>(string path) where Loader : ILoader<Key, Value>
	{
		string text = File.ReadAllText($"{ConfigManager.Config.dataPath}/{path}.json");
		return Newtonsoft.Json.JsonConvert.DeserializeObject<Loader>(text);
	}
	
	//...(중략)
}
```


# 게임룸 입장 및 관리


### **RoomManager.cs**
- 게임에 존재하는 게임룸을 관리합니다.
- 게임룸 생성, 해제, 특정 게임룸 검색 등의 기능을 수행합니다.
``` c#
public class RoomManager
{
	public static RoomManager Instance { get; } = new RoomManager();

	//dictionary에 존재하는 룸 저장 및 관리
	Dictionary<int, GameRoom> _rooms = new Dictionary<int, GameRoom>();
	object _lock = new object();
	int _roomId = 1;
		
	//게임룸 추가
	public GameRoom Add(int mapId)
	{
		GameRoom gameRoom = new GameRoom();
		gameRoom.Push(gameRoom.Init, mapId);

		lock (_lock)
		{
			gameRoom.RoomId = _roomId;
			_rooms.Add(_roomId, gameRoom);
			_roomId++;
		}

		return gameRoom;
	}

	//게임룸 삭제
	public bool Remove(int roomId)
	{
		lock (_lock)
		{
			return _rooms.Remove(roomId);
		}
	}

	//게임룸 검색
	public GameRoom Find(int roomId)
	{
		lock (_lock)
		{
			GameRoom room = null;
			if (_rooms.TryGetValue(roomId, out room))
				return room;

			return null;
		}
	}
}
```


### **GameRoom.cs**
- 게임룸 객체로 게임룸Id, 맵데이터, 게임룸 내에 존재하는 다양한 객체를 관리합니다.
- 초기화 시 특정 맵Id에 해당하는 맵데이터를 로드합니다.
- 플레이어의 이동 동기화, 스킬 처리, 판정 처리 등 게임룸 내에서 이루어지는 행위를 실질적으로 처리하는 객체입니다.
``` c#
public class GameRoom : JobSerializer
{
	//게임룸 Id
	public int RoomId { get; set; }

	//게임룸 내에 존재하는 객체들을 dictionary 형태로 관리한다.
	Dictionary<int, Player> _players = new Dictionary<int, Player>();
	Dictionary<int, Monster> _monsters = new Dictionary<int, Monster>();
	Dictionary<int, Projectile> _projectiles = new Dictionary<int, Projectile>();

	//맵 데이터
	public Map Map { get; private set; } = new Map();

	//게임룸 초기화
	public void Init(int mapId)
	{
		Map.LoadMap(mapId);
	}
		
	//...이하 생략
}
```
- 서버가 지정한 프레임레이트에 맞게 Update()가 주기적으로 호출됩니다.
- Update()에서는 게임룸 내 객체들의 상태를 업데이트하고, JobQueue 쌓인 작업(주로 패킷 처리)를 수행합니다. 
``` c#
public class GameRoom : JobSerializer
{
	//...(중략)
	
	public void Update()
	{
		//게임 내 객체 업데이트 처리
		foreach (Monster monster in _monsters.Values)
		{
			monster.Update();
		}

		foreach (Projectile projectile in _projectiles.Values)
		{
			projectile.Update();
		}

		//JobQueue에 쌓인 Job을 순차적으로 실행한다.
		Flush();
	}
	
	//...(중략)
}
``` 
- EnterGame(GameObject gameObject) 함수로 게임룸에 추가되는 객체를 저장하고 다른 클라이언트 세션에게 그 내용을 통보합니다.
``` c#
public class GameRoom : JobSerializer
{
	//...(중략)
	
	//게임 입장
	public void EnterGame(GameObject gameObject)
	{
		if (gameObject == null)
			return;

		GameObjectType type = ObjectManager.GetObjectTypeById(gameObject.Id);

		//플레이어일 경우
		if (type == GameObjectType.Player)
		{
			Player player = gameObject as Player;
			_players.Add(gameObject.Id, player);
			player.Room = this;

			Map.ApplyMove(player, new Vector2Int(player.CellPos.x, player.CellPos.y));

			// 본인한테 정보 전송
			S_EnterGame enterPacket = new S_EnterGame();
			enterPacket.Player = player.Info;
			player.Session.Send(enterPacket);

			S_Spawn spawnPacket = new S_Spawn();
			foreach (Player p in _players.Values)
			{
				if (player != p)
					spawnPacket.Objects.Add(p.Info);
			}

			foreach (Monster m in _monsters.Values)
				spawnPacket.Objects.Add(m.Info);

			foreach (Projectile p in _projectiles.Values)
				spawnPacket.Objects.Add(p.Info);

			player.Session.Send(spawnPacket);
		}
		//몬스터 NPC일 경우
		else if (type == GameObjectType.Monster)
		{
			Monster monster = gameObject as Monster;
			_monsters.Add(gameObject.Id, monster);
			monster.Room = this;

			Map.ApplyMove(monster, new Vector2Int(monster.CellPos.x, monster.CellPos.y));
		}
		//투사체일 경우
		else if (type == GameObjectType.Projectile)
		{
			Projectile projectile = gameObject as Projectile;
			_projectiles.Add(gameObject.Id, projectile);
			projectile.Room = this;
		}
	
		// 타인한테 정보 전송
		S_Spawn spawnPacket = new S_Spawn();
		spawnPacket.Objects.Add(gameObject.Info);
		foreach (Player p in _players.Values)
		{
			if (p.Id != gameObject.Id)
				p.Session.Send(spawnPacket);
		}
	}
	
	//...(중략)
}
```
- LeaveGame(int objectId) 함수를 통해 게임룸에서 삭제 및 퇴장하는 객체를 해체하고 다른 클라이언트 세션에게 그 내용을 통보합니다.
``` c#
public class GameRoom : JobSerializer
{
	//...(중략)

	//게임 퇴장
	public void LeaveGame(int objectId)
	{
		GameObjectType type = ObjectManager.GetObjectTypeById(objectId);

		if (type == GameObjectType.Player)
		{
			Player player = null;
			if (_players.Remove(objectId, out player) == false)
				return;

			Map.ApplyLeave(player);
			player.Room = null;

			// 본인한테 정보 전송
			S_LeaveGame leavePacket = new S_LeaveGame();
			player.Session.Send(leavePacket);
		}
		else if (type == GameObjectType.Monster)
		{
			Monster monster = null;
			if (_monsters.Remove(objectId, out monster) == false)
				return;

			Map.ApplyLeave(monster);
			monster.Room = null;
		}
		else if (type == GameObjectType.Projectile)
		{
			Projectile projectile = null;
			if (_projectiles.Remove(objectId, out projectile) == false)
				return;

			projectile.Room = null;
		}

		// 타인한테 정보 전송
		{
			S_Despawn despawnPacket = new S_Despawn();
			despawnPacket.ObjectIds.Add(objectId);
			foreach (Player p in _players.Values)
			{
				if (p.Id != objectId)
					p.Session.Send(despawnPacket);
			}
		}
	}

	//...(중략)
}
```


# 맵 
(캡처 필요) 그리드 형태의 맵
### **Map.cs**
- 맵은 2d 그리드 형태로 Map 객체는 맵에 대한 모든 데이터를 담고 있습니다.
- Map 객체는 맵의 최소/최대 좌표, 맵의 크기, 장애물들에 대한 정보를 갖고 있습니다.
- ApplyMove()를 호출하여 캐릭터를 현재 좌표에서 목표 좌표로 이동시킵니다.
``` c#
public class Map
{
	//맵 최소/최대 좌표
	public int MinX { get; set; }
	public int MaxX { get; set; }
	public int MinY { get; set; }
	public int MaxY { get; set; }

	//맵 크기
	public int SizeX { get { return MaxX - MinX + 1; } }
	public int SizeY { get { return MaxY - MinY + 1; } }

	//장애물들에 대한 정보
	bool[,] _collision;
	GameObject[,] _objects;

	//목표 좌표로 이동 가능한지 확인
	public bool CanGo(Vector2Int cellPos, bool checkObjects = true)
	{
		if (cellPos.x < MinX || cellPos.x > MaxX)
			return false;
		if (cellPos.y < MinY || cellPos.y > MaxY)
			return false;

		int x = cellPos.x - MinX;
		int y = MaxY - cellPos.y;
		return !_collision[y, x] && (!checkObjects || _objects[y, x] == null);
	}

	//목표 좌표로 도착 처리
	public bool ApplyMove(GameObject gameObject, Vector2Int dest)
	{
		ApplyLeave(gameObject);

		if (gameObject.Room == null)
			return false;
		if (gameObject.Room.Map != this)
			return false;

		PositionInfo posInfo = gameObject.PosInfo;
		if (CanGo(dest, true) == false)
			return false;

		{
			int x = dest.x - MinX;
			int y = MaxY - dest.y;
			_objects[y, x] = gameObject;
		}

		// 실제 좌표 이동
		posInfo.PosX = dest.x;
		posInfo.PosY = dest.y;
		return true;
	}
	
	//특정 좌표에서 객체 떠남 처리
	public bool ApplyLeave(GameObject gameObject)
	{
		if (gameObject.Room == null)
			return false;
		if (gameObject.Room.Map != this)
			return false;

		PositionInfo posInfo = gameObject.PosInfo;
		if (posInfo.PosX < MinX || posInfo.PosX > MaxX)
			return false;
		if (posInfo.PosY < MinY || posInfo.PosY > MaxY)
			return false;

		{
			int x = posInfo.PosX - MinX;
			int y = MaxY - posInfo.PosY;
			if (_objects[y, x] == gameObject)
				_objects[y, x] = null;
		}

		return true;
	}
}
```

# JobQueue 디자인 패턴 
- (캡처 필요) 디자인 패턴 도식화
### **JobSerializer.cs**
- 패킷 핸들러 처리는 서버 lock을 최소화 하기 위해 Command 패턴을 사용합니다.
``` c#
//패킷 핸들러 처리는 서버 lock을 최소화 하기 위해 JobQueue 방식을 사용합니다.
public class JobSerializer
{
	JobTimer _timer = new JobTimer();
	Queue<IJob> _jobQueue = new Queue<IJob>();
	object _lock = new object();
	bool _flush = false;

	//...(중략)
}
```
- 핸들러를 Job으로 변환하여 _jobQueue에 넣어줍니다.
``` c#
public class JobSerializer
{
	//...(중략)
	
	public void Push(IJob job)
	{
		lock (_lock)
		{
			_jobQueue.Enqueue(job);
		}
	}
	
	//...(중략)
}
```
- 게임룸에서 특정 시간 주기로 Tick이 발동되며 Flush를 호출합니다.
``` c#
class Program
{
	static Listener _listener = new Listener();
	static List<System.Timers.Timer> _timers = new List<System.Timers.Timer>();

	static void TickRoom(GameRoom room, int tick = 100)
	{
		var timer = new System.Timers.Timer();
		timer.Interval = tick;
		timer.Elapsed += ((s, e) => { room.Update(); });
		timer.AutoReset = true;
		timer.Enabled = true;

		_timers.Add(timer);
	}
	
	//...(중략)
}
```
- Flush()에서 _jobQueue에 쌓여있는 것들을 차례로 실행합니다.
``` c#	
public class JobSerializer
{
	//...(중략)
	
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
	
	//...(중략)
}
```


# 플레이어 이동
(캡쳐 필요)
### **GameRoom.cs**
- 클라이언트 세션으로부터 C_Move 패킷을 받으면 해당 플레이어에 대한 이동을 처리합니다.
- 목표 좌표로 이동 가능한지 검사한 후 이동 시킨 후, 이동 결과를 다른 클라이언트 세션에 통보해 동기화 시킵니다.
``` c#
public class GameRoom : JobSerializer
{
	//...(중략)
	
	//이동 처리
	public void HandleMove(Player player, C_Move movePacket)
	{
		if (player == null)
			return;

		PositionInfo movePosInfo = movePacket.PosInfo;
		ObjectInfo info = player.Info;

		// 다른 좌표로 이동할 경우, 갈 수 있는지 체크
		if (movePosInfo.PosX != info.PosInfo.PosX || movePosInfo.PosY != info.PosInfo.PosY)
		{
			if (Map.CanGo(new Vector2Int(movePosInfo.PosX, movePosInfo.PosY)) == false)
				return;
		}

		info.PosInfo.State = movePosInfo.State;
		info.PosInfo.MoveDir = movePosInfo.MoveDir;
		Map.ApplyMove(player, new Vector2Int(movePosInfo.PosX, movePosInfo.PosY));

		// 다른 플레이어한테도 브로드캐스팅하여 동기화
		S_Move resMovePacket = new S_Move();
		resMovePacket.ObjectId = player.Info.ObjectId;
		resMovePacket.PosInfo = movePacket.PosInfo;
		Broadcast(resMovePacket);
	}
	
	//...(중략)
}
```


# 플레이어 스킬 발동 및 판정 처리
(캡쳐 필요)
### **GameRoom.cs**
- 클라이언트 세션으로부터 C_Skill 패킷을 받으면 해당 플레이어에 대한 스킬 처리를 수행합니다.
- 스킬이 발동되었다는 것을 다른 클라이언트 세션에 통보합니다.
- 스킬 타입에 맞게 정의된 처리를 해줍니다.
- 근접 공격의 경우 바로 피격 판정 처리를 진행하지만, 투사체 공격의 경우 투사체를 생성만 해주고 실제 피격 처리는 투사체의 로직에서 처리합니다.
``` c#
public class GameRoom : JobSerializer
{
	//...(중략)
	
	//스킬 처리
	public void HandleSkill(Player player, C_Skill skillPacket)
	{
		if (player == null)
			return;

		ObjectInfo info = player.Info;
		if (info.PosInfo.State != CreatureState.Idle)
			return;

		info.PosInfo.State = CreatureState.Skill;
		
		//다른 클라이언트 세션에 스킬 발동 통보
		S_Skill skill = new S_Skill() { Info = new SkillInfo() };
		skill.ObjectId = info.ObjectId;
		skill.Info.SkillId = skillPacket.Info.SkillId;
		Broadcast(skill);

		Data.Skill skillData = null;
		if (DataManager.SkillDict.TryGetValue(skillPacket.Info.SkillId, out skillData) == false)
			return;

		//근접 스킬, 투사체 스킬 등 스킬타입에 따라 다르게 처리한다.
		switch (skillData.skillType)
		{
			case SkillType.SkillAuto: //일반 근접 공격
				{
					Vector2Int skillPos = player.GetFrontCellPos(info.PosInfo.MoveDir);
					GameObject target = Map.Find(skillPos);
					//근접 공격은 바로 피격 처리
					if (target != null)
					{
						Console.WriteLine("Hit GameObject !");
						target.OnDamaged(player, player.Stat.Attack);
					}
				}
				break;
			case SkillType.SkillProjectile: //원거리 화살 공격
				{
					//화살 공격은 화살 투사체만 생성 해주고, 화살 로직에서 충돌 및 피격처리를 한다.
					Arrow arrow = ObjectManager.Instance.Add<Arrow>();
					if (arrow == null)
						return;

					arrow.Owner = player;
					arrow.Data = skillData;
					arrow.PosInfo.State = CreatureState.Moving;
					arrow.PosInfo.MoveDir = player.PosInfo.MoveDir;
					arrow.PosInfo.PosX = player.PosInfo.PosX;
					arrow.PosInfo.PosY = player.PosInfo.PosY;
					arrow.Speed = skillData.projectile.speed;
					Push(EnterGame, arrow);
				}
				break;
		}
	}
	
	//...(중략)
}
```


### **GameObject.cs**
- GameObject는 게임 내 존재하는 모든 오브젝트의 상위 클래스입니다.
- Player도 GameObject의 자식 클래스로 여기서 데미지 차감 처리, 사망 처리가 이루어집니다.
- OnDamaged 함수에서 데미지 처리를, 만약 HP가 0이하가 되면 OnDead 함수에서 사망 처리를 이어서 수행합니다.
- 데미지 처리, 사망 처리에서 각각 다른 클라이언트 세션에게 해당 내용을 통보합니다. 
``` c#
public class GameObject
{
	//...(중략)
	
	//데미지 처리
	public virtual void OnDamaged(GameObject attacker, int damage)
	{
		if (Room == null)
			return;

		Stat.Hp = Math.Max(Stat.Hp - damage, 0);

		//다른 클라이언트 세션에  통보
		S_ChangeHp changePacket = new S_ChangeHp();
		changePacket.ObjectId = Id;
		changePacket.Hp = Stat.Hp;
		Room.Broadcast(changePacket);

		//HP가 0이하가 되면 사망 처리를 위해 OnDead를 호출한다.
		if (Stat.Hp <= 0)
		{
			OnDead(attacker);
		}
	}

	//사망 처리
	public virtual void OnDead(GameObject attacker)
	{
		if (Room == null)
			return;

		//다른 클라이언트 세션에 사망 통보
		S_Die diePacket = new S_Die();
		diePacket.ObjectId = Id;
		diePacket.AttackerId = attacker.Id;
		Room.Broadcast(diePacket);

		//사망 처리되면 플레이어를 게임룸에서 퇴장시킨다.
		GameRoom room = Room;
		room.LeaveGame(Id);
	}
	
	//...(중략)
}
```


# NPC->플레이어 Search AI
(캡쳐 필요)
### **Monster.cs**
- 몬스터 NPC AI는 FSM (Finite State Machine)으로 구현되어 있습니다.
- 정해진 프레임마다 Update()가 호출되며 현재 State에 따른 행동을 수행합니다.
``` c#
public class Monster : GameObject
{
	public Monster()
	{
		ObjectType = GameObjectType.Monster;
	
		Stat.Level = 1;
		Stat.Hp = 100;
		Stat.MaxHp = 100;
		Stat.Speed = 5.0f;
	
		State = CreatureState.Idle;
	}
	
	// FSM (Finite State Machine)
	public override void Update()
	{
		switch (State)
		{
			case CreatureState.Idle:
				UpdateIdle();
				break;
			case CreatureState.Moving:
				UpdateMoving();
				break;
			case CreatureState.Skill:
				UpdateSkill();
				break;
			case CreatureState.Dead:
				UpdateDead();
				break;
		}
	}
	
	//...(중략)
}
```
- Idle 상태에서는 정해진 범위 내에 플레이어가 있는지 탐색합니다.
- 플레이어를 발견하면, 추적해야할 target으로 지정하고 몬스터의 상태가 Idle에서 Moving으로 업데이트 됩니다.
``` c#
public class Monster : GameObject
{
	//...(중략)
		
	Player _target;
	int _searchCellDist = 10;
	int _chaseCellDist = 20;
	
	protected virtual void UpdateIdle()
	{
		if (_nextSearchTick > Environment.TickCount64)
			return;
			
		_nextSearchTick = Environment.TickCount64 + 1000;

		//탐색 범위(_searchCellDist)에 플레이어가 있으면 target으로 설정
		Player target = Room.FindPlayer(p =>
		{
			Vector2Int dir = p.CellPos - CellPos;
			return dir.cellDistFromZero <= _searchCellDist;
		});

		if (target == null)
			return;

		_target = target;
		
		//target 있으면 Moving으로 상태 변화
		State = CreatureState.Moving;
	}
	
	//...(중략)
}
```
- 최적의 Map.cs의 FindPath() 함수로 최적의 경로를 탐색한 후, 그 경로를 따라 이동합니다.
- 플레이어가 너무 멀어지거나 방에서 퇴장하는 등의 예외 상황에 주의하며, 예외 처리를 꼼꼼히 해주어야 합니다.
``` c#
public class Monster : GameObject
{
	//...(중략)
	
	int _skillRange = 1;
	long _nextMoveTick = 0;
	protected virtual void UpdateMoving()
	{
		if (_nextMoveTick > Environment.TickCount64)
			return;
		int moveTick = (int)(1000 / Speed);
		_nextMoveTick = Environment.TickCount64 + moveTick;

		//target이 유효한지 확인
		if (_target == null || _target.Room != Room)
		{
			_target = null;
			State = CreatureState.Idle;
			BroadcastMove();
			return;
		}

		Vector2Int dir = _target.CellPos - CellPos;
		int dist = dir.cellDistFromZero;
		if (dist == 0 || dist > _chaseCellDist)
		{
			_target = null;
			State = CreatureState.Idle;
			BroadcastMove();
			return;
		}

		//최단 경로 탐색(A* 알고리즘 기반) 
		List<Vector2Int> path = Room.Map.FindPath(CellPos, _target.CellPos, checkObjects: false);
		if (path.Count < 2 || path.Count > _chaseCellDist)
		{
			_target = null;
			State = CreatureState.Idle;
			BroadcastMove();
			return;
		}

		// 스킬로 넘어갈지 체크
		if (dist <= _skillRange && (dir.x == 0 || dir.y == 0))
		{
			_coolTick = 0;
			State = CreatureState.Skill;
			return;
		}

		// 이동
		Dir = GetDirFromVec(path[1] - CellPos);
		Room.Map.ApplyMove(this, path[1]);
		BroadcastMove();
	}
	
	//...(중략)
}
```
- 최적 경로 탐색은 A* 알고리즘을 기반으로 구현되어 있습니다.
``` c#
public class Map
{
	//...(중략)

	// U D L R
	int[] _deltaY = new int[] { 1, -1, 0, 0 };
	int[] _deltaX = new int[] { 0, 0, -1, 1 };
	int[] _cost = new int[] { 10, 10, 10, 10 };

	public List<Vector2Int> FindPath(Vector2Int startCellPos, Vector2Int destCellPos, bool checkObjects = true)
	{
		List<Pos> path = new List<Pos>();

		// 점수 매기기
		// F = G + H
		// F = 최종 점수 (작을 수록 좋음, 경로에 따라 달라짐)
		// G = 시작점에서 해당 좌표까지 이동하는데 드는 비용 (작을 수록 좋음, 경로에 따라 달라짐)
		// H = 목적지에서 얼마나 가까운지 (작을 수록 좋음, 고정)

		// (y, x) 이미 방문했는지 여부 (방문 = closed 상태)
		bool[,] closed = new bool[SizeY, SizeX]; // CloseList

		// (y, x) 가는 길을 한 번이라도 발견했는지
		// 발견X => MaxValue
		// 발견O => F = G + H
		int[,] open = new int[SizeY, SizeX]; // OpenList
		for (int y = 0; y < SizeY; y++)
			for (int x = 0; x < SizeX; x++)
				open[y, x] = Int32.MaxValue;

		Pos[,] parent = new Pos[SizeY, SizeX];

		// 오픈리스트에 있는 정보들 중에서, 가장 좋은 후보를 빠르게 뽑아오기 위한 도구
		PriorityQueue<PQNode> pq = new PriorityQueue<PQNode>();

		// CellPos -> ArrayPos
		Pos pos = Cell2Pos(startCellPos);
		Pos dest = Cell2Pos(destCellPos);

		// 시작점 발견 (예약 진행)
		open[pos.Y, pos.X] = 10 * (Math.Abs(dest.Y - pos.Y) + Math.Abs(dest.X - pos.X));
		pq.Push(new PQNode() { F = 10 * (Math.Abs(dest.Y - pos.Y) + Math.Abs(dest.X - pos.X)), G = 0, Y = pos.Y, X = pos.X });
		parent[pos.Y, pos.X] = new Pos(pos.Y, pos.X);

		while (pq.Count > 0)
		{
			// 제일 좋은 후보를 찾는다
			PQNode node = pq.Pop();
			// 동일한 좌표를 여러 경로로 찾아서, 더 빠른 경로로 인해서 이미 방문(closed)된 경우 스킵
			if (closed[node.Y, node.X])
				continue;

			// 방문한다
			closed[node.Y, node.X] = true;
			// 목적지 도착했으면 바로 종료
			if (node.Y == dest.Y && node.X == dest.X)
				break;

			// 상하좌우 등 이동할 수 있는 좌표인지 확인해서 예약(open)한다
			for (int i = 0; i < _deltaY.Length; i++)
			{
				Pos next = new Pos(node.Y + _deltaY[i], node.X + _deltaX[i]);

				// 유효 범위를 벗어났으면 스킵
				// 벽으로 막혀서 갈 수 없으면 스킵
				if (next.Y != dest.Y || next.X != dest.X)
				{
					if (CanGo(Pos2Cell(next), checkObjects) == false) // CellPos
						continue;
				}

				// 이미 방문한 곳이면 스킵
				if (closed[next.Y, next.X])
					continue;

				// 비용 계산
				int g = 0;// node.G + _cost[i];
				int h = 10 * ((dest.Y - next.Y) * (dest.Y - next.Y) + (dest.X - next.X) * (dest.X - next.X));
				// 다른 경로에서 더 빠른 길 이미 찾았으면 스킵
				if (open[next.Y, next.X] < g + h)
					continue;

				// 예약 진행
				open[dest.Y, dest.X] = g + h;
				pq.Push(new PQNode() { F = g + h, G = g, Y = next.Y, X = next.X });
				parent[next.Y, next.X] = new Pos(node.Y, node.X);
			}
		}

		return CalcCellPathFromParent(parent, dest);
	}
	
	//...(중략)
}
```


# NPC->플레이어 Skill AI
### **Monster.cs**
- 몬스터 NPC가 플레이어를 추척하고 따라와 스킬을 쓸 수 있을 만큼 따라잡으면, 상태를 Skill로 업데이트 합니다.
- UpdateSkill() 함수에서 플레이어를 대상으로 스킬을 발동합니다.
``` c#
public class Monster : GameObject
{
	//...(중략)

	long _coolTick = 0;
	protected virtual void UpdateSkill()
	{
		if (_coolTick == 0)
		{
			// 유효한 타겟인지
			if (_target == null || _target.Room != Room || _target.Hp == 0)
			{
				_target = null;
				State = CreatureState.Moving;
				BroadcastMove();
				return;
			}

			// 스킬이 아직 사용 가능한지
			Vector2Int dir = (_target.CellPos - CellPos);
			int dist = dir.cellDistFromZero;
			bool canUseSkill = (dist <= _skillRange && (dir.x == 0 || dir.y == 0));
			if (canUseSkill == false)
			{
				State = CreatureState.Moving;
				BroadcastMove();
				return;
			}

			// 타게팅 방향 주시
			MoveDir lookDir = GetDirFromVec(dir);
			if (Dir != lookDir)
			{
				Dir = lookDir;
				BroadcastMove();
			}

			Skill skillData = null;
			DataManager.SkillDict.TryGetValue(1, out skillData);

			// 데미지 판정
			_target.OnDamaged(this, skillData.damage + Stat.Attack);

			// 스킬 사용 Broadcast
			S_Skill skill = new S_Skill() { Info = new SkillInfo() };
			skill.ObjectId = Id;
			skill.Info.SkillId = skillData.id;
			Room.Broadcast(skill);

			// 스킬 쿨타임 적용
			int coolTick = (int)(1000 * skillData.cooldown);
			_coolTick = Environment.TickCount64 + coolTick;
		}

		if (_coolTick > Environment.TickCount64)
			return;

		_coolTick = 0;
	}
	
	//...(중략)
}
```
