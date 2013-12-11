using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Floe.UI.Settings
{
	public partial class FormattingSettingsControl : UserControl
	{
		public class FontFamilyListItem : ComboBoxItem
		{
			public string FontFamilyName { get; private set; }

			public FontFamilyListItem(FontFamily font)
			{
				this.Content = this.FontFamilyName = font.Source;
				this.FontFamily = font;
			}
		}

		public ICollection<string> FontFamilyItems { get; private set; }
		public ICollection<string> FontWeightItems { get; private set; }

		public FormattingSettingsControl()
		{
			this.FontFamilyItems = (from font in Fonts.SystemFontFamilies
									orderby font.Source
									select font.Source).ToList();

			InitializeComponent();


			this.FontWeightItems = new[] {
				"Normal", "Bold", "Black"
			};
		}

		protected override void OnInitialized(EventArgs e)
		{
			base.OnInitialized(e);
		}

        /// <summary>
        /// Ensure that only numbers get input into this box. Probably not the most elegant solution, but it works.
        /// </summary>
        private void MaxImgWidth_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (e.Text.ToCharArray().Any(c => !Char.IsDigit(c))) e.Handled = true;
        }
	}
}
