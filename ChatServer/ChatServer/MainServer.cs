using SuperSocket.SocketBase;
using SuperSocket.SocketBase.Protocol;
using System;
using System.Collections.Generic;
using System.Text;

// TODO: 주기적으로 접속한 세션이 패킷을 주고 받았는지 조사
namespace ChatServer
{

    public class MainServer : AppServer<ClientSession, EFBinaryRequestInfo>
    {
        public static ChatServerOption ServerOption;
        public static SuperSocket.SocketBase.Logging.ILog MainLogger;

        SuperSocket.SocketBase.Config.IServerConfig m_Config;


        public MainServer()
            : base (new DefaultReceiveFilterFactory<ReceiveFilter, EFBinaryRequestInfo>())
        {
          //  NewSessionConnected += new SessionHandler<ClientSession>(OnConnected);
           // SessionClosed += new SessionHandler<ClientSession, CloseReason>(OnClosed);
           // NewRequestReceived += new RequestHandler<ClientSession, EFBinaryRequestInfo>(OnPacketReceived);
        }


        public void InitConfig(ChatServerOption option)
        {
            ServerOption = option;

            m_Config = new SuperSocket.SocketBase.Config.ServerConfig
            {
                Name = option.Name , 
                Ip = "Any" , 
                Port = option.Port,
                Mode = SocketMode.Tcp ,
                MaxConnectionNumber = option.MaxConnectionNumber,
                MaxRequestLength = option.MaxRequestLength,
                ReceiveBufferSize = option.ReceiveBufferSize,
                SendBufferSize = option.SendBufferSize
            };
        }

        public void CreateStartServer()
        {
            try
            {
                bool bResult = Setup(new SuperSocket.SocketBase.Config.RootConfig(), m_Config, logFactory: new SuperSocket.SocketBase.Logging.NLogLogFactory());
                
                if (bResult == false)
                {
                    Console.WriteLine("[ERROR] 서버 네트워크 설정 실패");
                    return;
                }
                else
                {
                    MainLogger = base.Logger;
                    MainLogger.Info("서버 초기화 성공");
                }

                Start();

                MainLogger.Info("서버 생성 성공");
            }
            catch(Exception ex)
            {
                Console.WriteLine($"[ERROR] 서버 생성 실패 : {ex.ToString()}");
            }
        }


    }
}
