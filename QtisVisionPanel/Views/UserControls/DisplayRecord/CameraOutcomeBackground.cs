using System.Windows.Controls;
using System.Windows.Media;

namespace QtisVisionPanel.Views.UserControls.DisplayRecord
{
    /// <summary>
    /// Colora la card delle feature di una vista camera con l'esito globale del pezzo.
    /// Serve all'operatore per leggere lo stato dell'ispezione da lontano, senza dover
    /// distinguere le singole icone.
    /// </summary>
    internal static class CameraOutcomeBackground
    {
        // Il fondo usa tinte chiare di proposito: le icone delle ispezioni sono rosse
        // (#E30613) e su un rosso saturo sparirebbero. La distanza di lettura la porta il
        // bordo a tinta piena, che non compete con le icone.
        private static readonly Brush GoodBackground = Frozen(0xC8, 0xE6, 0xC9);
        private static readonly Brush NoGoodBackground = Frozen(0xFF, 0xCD, 0xD2);
        private static readonly Brush NeutralBackground = Brushes.Transparent;

        private static readonly Brush GoodBorder = Frozen(0x2E, 0x7D, 0x32);
        private static readonly Brush NoGoodBorder = Frozen(0xC6, 0x28, 0x28);
        private static readonly Brush NeutralBorder = Frozen(0xBD, 0xC3, 0xC7);

        private static Brush Frozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze(); // condiviso fra viste e thread UI: deve essere immutabile
            return brush;
        }

        /// <param name="isGood">
        /// true = pezzo conforme, false = scarto, null = nessun esito attendibile
        /// (pezzo non classificato o stato iniziale): la card torna neutra.
        /// </param>
        public static void Apply(Border card, bool? isGood)
        {
            if (card == null)
            {
                return;
            }

            if (isGood == true)
            {
                card.Background = GoodBackground;
                card.BorderBrush = GoodBorder;
            }
            else if (isGood == false)
            {
                card.Background = NoGoodBackground;
                card.BorderBrush = NoGoodBorder;
            }
            else
            {
                card.Background = NeutralBackground;
                card.BorderBrush = NeutralBorder;
            }
        }
    }
}
