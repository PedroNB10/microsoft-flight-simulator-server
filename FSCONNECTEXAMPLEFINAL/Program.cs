using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using CTrue.FsConnect;
using Microsoft.FlightSimulator.SimConnect;
using Newtonsoft.Json; // Certifique-se de instalar o pacote Newtonsoft.Json

namespace FlightSimulatorHttpServer
{
    class Program
    {
        private static HttpListener httpListener;
        private static FsConnect fsConnect;
        private static int planeInfoDefinitionId;
        private static bool isConnected = false;
        private static bool isConnecting = false; // Novo sinalizador
        private static PlaneInfoResponse? latestPlaneData;
        private static object dataLock = new object();

        static async Task Main(string[] args)
        {
            // Inicializa o servidor HTTP
            httpListener = new HttpListener();
            httpListener.Prefixes.Add("http://localhost:5000/");
            httpListener.Start();
            Console.WriteLine("Servidor HTTP iniciado em http://localhost:5000/");

            // Aceita conexões HTTP em um loop
            _ = Task.Run(HandleHttpRequestsAsync);

            // Inicializa o FsConnect
            fsConnect = new FsConnect();
            fsConnect.ConnectionChanged += FsConnect_ConnectionChanged;
            fsConnect.FsDataReceived += FsConnect_FsDataReceived;

            // Tenta conectar ao Flight Simulator
            _ = Task.Run(ConnectToFlightSimulatorAsync);

            // Mantém o programa em execução
            await Task.Delay(Timeout.Infinite);
        }

        private static async Task HandleHttpRequestsAsync()
        {
            while (true)
            {
                var context = await httpListener.GetContextAsync();

                if (context.Request.HttpMethod == "GET")
                {
                    // Retorna os dados do avião em JSON
                    string responseString;

                    lock (dataLock)
                    {
                        if (latestPlaneData != null)
                        {
                            var planeData = new
                            {
                                latestPlaneData.Value.Title,
                                latestPlaneData.Value.PlaneLatitude,
                                latestPlaneData.Value.PlaneLongitude,
                                latestPlaneData.Value.PlaneAltitude,
                                latestPlaneData.Value.PlaneHeadingDegreesMagnetic,
                                latestPlaneData.Value.AirspeedTrue,
                                latestPlaneData.Value.VerticalSpeed,
                                timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")

                        };

                            responseString = JsonConvert.SerializeObject(planeData);
                        }
                        else
                        {
                            // Envia todos os parâmetros com valor zero
                            var planeData = new
                            {
                                Title = "",
                                Latitude = 0.0,
                                Longitude = 0.0,
                                Altitude = 0.0,
                                Heading = 0.0,
                                Airspeed = 0.0,
                                VerticalSpeed = 0.0 
                            };

                            responseString = JsonConvert.SerializeObject(planeData);
                        }
                    }

                    byte[] buffer = Encoding.UTF8.GetBytes(responseString);
                    context.Response.ContentType = "application/json";
                    context.Response.ContentEncoding = Encoding.UTF8;
                    context.Response.ContentLength64 = buffer.Length;
                    await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    context.Response.OutputStream.Close();
                }
                else
                {
                    context.Response.StatusCode = 405; // Método não permitido
                    context.Response.Close();
                }
            }
        }

        private static async Task ConnectToFlightSimulatorAsync()
        {
            isConnecting = true; // Indica que uma tentativa de conexão está em andamento

            while (!isConnected)
            {
                try
                {
                    Console.WriteLine("Tentando conectar ao Flight Simulator...");
                    fsConnect.Connect("FsConnectApp");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erro ao conectar: {ex.Message}");
                }

                if (!isConnected)
                {
                    // Aguarda um tempo antes de tentar novamente
                    await Task.Delay(5000);
                }
            }

            isConnecting = false; // Tentativa de conexão concluída
        }

        private static void FsConnect_ConnectionChanged(object sender, bool connected)
        {
            isConnected = connected;
            if (connected)
            {
                Console.WriteLine("Conectado ao Flight Simulator.");

                // Registra a definição de dados
                planeInfoDefinitionId = fsConnect.RegisterDataDefinition<PlaneInfoResponse>();

                // Solicita dados da aeronave periodicamente
                fsConnect.RequestDataOnSimObject(
                    Requests.PlaneInfoRequest,
                    planeInfoDefinitionId,
                    SimConnect.SIMCONNECT_OBJECT_ID_USER,
                    FsConnectPeriod.Second, // Solicita dados a cada segundo
                    FsConnectDRequestFlag.Default, // Recebe dados em cada período
                    0,
                    0,
                    0
                );
            }
            else
            {
                Console.WriteLine("Desconectado do Flight Simulator.");
                isConnected = false; // Marca como desconectado

                // Tentar reconectar se não houver uma conexão em andamento
                if (!isConnecting)
                {
                    _ = Task.Run(ConnectToFlightSimulatorAsync);
                }
            }
        }

        private static void FsConnect_FsDataReceived(object sender, FsDataReceivedEventArgs e)
        {
            if (e.RequestId == (uint)Requests.PlaneInfoRequest && e.Data.Count > 0)
            {
                var data = (PlaneInfoResponse)e.Data[0];

                lock (dataLock)
                {
                    latestPlaneData = data;
                }

                Console.WriteLine($"Dados atualizados: {DateTime.Now}");
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        public struct PlaneInfoResponse
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string Title;
            [SimVar(UnitId = FsUnit.Degree)]
            public double PlaneLatitude;
            [SimVar(UnitId = FsUnit.Degree)]
            public double PlaneLongitude;
            [SimVar(UnitId = FsUnit.Feet)]
            public double PlaneAltitude;
            [SimVar(NameId = FsSimVar.PlaneHeadingDegreesMagnetic, UnitId = FsUnit.Degree)]
            public double PlaneHeadingDegreesMagnetic;
            [SimVar(NameId = FsSimVar.AirspeedTrue, UnitId = FsUnit.Knot)]
            public double AirspeedTrue;
            [SimVar(NameId = FsSimVar.VerticalSpeed, UnitId = FsUnit.FeetPerMinute)]
            public double VerticalSpeed;
        }

        public enum Requests
        {
            PlaneInfoRequest = 0
        }
    }
}
