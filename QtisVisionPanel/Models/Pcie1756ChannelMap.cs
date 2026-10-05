namespace QtisVisionPanel.Models
{
    /// <summary>
    /// Unica fonte dello spazio canali della scheda Advantech PCIE-1756-BE.
    /// La scheda ha 32 DI (CON1) + 32 DO (CON2); in DAQNavi ogni porta DIO e' da 8 bit,
    /// quindi 4 porte DI e 4 porte DO. I canali PCIE-1884 (100-103) sono gestiti
    /// da rami dedicati in AdvantechDeviceManager e restano invariati.
    /// </summary>
    public static class Pcie1756ChannelMap
    {
        /// <summary>Bit per porta DIO DAQNavi.</summary>
        public const int BitsPerPort = 8;

        /// <summary>Porte DI reali della PCIE-1756 (DI00-DI31).</summary>
        public const int DiPortCount = 4;

        /// <summary>Porte DO reali della PCIE-1756 (DO00-DO31).</summary>
        public const int DoPortCount = 4;

        /// <summary>Canali DI validi: 0..31.</summary>
        public const int DiChannelCount = DiPortCount * BitsPerPort; // 32

        /// <summary>Canali DO validi: 0..31.</summary>
        public const int DoChannelCount = DoPortCount * BitsPerPort; // 32

        /// <summary>
        /// Lunghezza legacy dei buffer byte[] della PCIE-1756: contratto di
        /// <see cref="IIODeviceManager"/> (ReadAllInputsAsync / ReadAllOutputsAsync /
        /// WriteOutputsAsync) e formato persistito in io_simulation_state.xml.
        /// Solo i byte 0..3 corrispondono a porte hardware; i byte 4..7 valgono sempre 0.
        /// Non ridurre: i file di simulazione esistenti e le build precedenti usano 8 byte.
        /// </summary>
        public const int LegacyPortBufferLength = 8;

        /// <summary>Primo canale logico della PCIE-1884 (100).</summary>
        public const int Pcie1884ChannelBase = 100;

        /// <summary>Numero di canali DI/DO logici della PCIE-1884 (100-103).</summary>
        public const int Pcie1884ChannelCount = 4;

        /// <summary>True se il canale e' un DI valido della PCIE-1756 (0..31).</summary>
        public static bool IsValidDiChannel(int channel) => channel >= 0 && channel < DiChannelCount;

        /// <summary>True se il canale e' un DO valido della PCIE-1756 (0..31).</summary>
        public static bool IsValidDoChannel(int channel) => channel >= 0 && channel < DoChannelCount;

        /// <summary>True se il canale appartiene alla PCIE-1884 (100..103).</summary>
        public static bool IsPcie1884Channel(int channel) =>
            channel >= Pcie1884ChannelBase && channel < Pcie1884ChannelBase + Pcie1884ChannelCount;

        /// <summary>True se il canale di ingresso e' valido: DI PCIE-1756 (0..31) o PCIE-1884 (100..103).</summary>
        public static bool IsValidInputChannel(int channel) => IsValidDiChannel(channel) || IsPcie1884Channel(channel);

        /// <summary>True se il canale di uscita e' valido: DO PCIE-1756 (0..31) o PCIE-1884 (100..103).</summary>
        public static bool IsValidOutputChannel(int channel) => IsValidDoChannel(channel) || IsPcie1884Channel(channel);
    }
}
