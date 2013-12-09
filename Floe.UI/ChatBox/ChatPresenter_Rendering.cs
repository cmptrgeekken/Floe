using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.TextFormatting;
using System.Windows.Threading;

namespace Floe.UI
{
    public partial class ChatPresenter : ChatBoxBase, IScrollInfo
    {
        private const double SeparatorPadding = 6.0;
        private const int TextProcessingBatchSize = 50;
        private const float MinNickBrightness = .2f;
        private const float NickBrightnessBand = .2f;

        private class Block
        {
            public ChatLine Source { get; set; }
            public Brush Foreground { get; set; }

            public string TimeString { get; set; }
            public string NickString { get; set; }

            public TextLine Time { get; set; }
            public TextLine Nick { get; set; }
            public TextLine[] Text { get; set; }
            public ImageSource Image { get; set; }
            public double ImageWidth { get; set; }
            public double ImageHeight { get; set; }

            public int CharStart { get; set; }
            public int CharEnd { get; set; }
            public double Y { get; set; }
            public double NickX { get; set; }
            public double TextX { get; set; }

            public double GetTotalHeight(double lineHeight)
            {
                return Text == null ? 0.0 : Text.Sum(t => t.Height);
            }

            public int GetTotalLines(double lineHeight, bool includeImages)
            {
                if (Text == null)
                {
                    return 0;
                }

                var lines = Text.Length;
                if (includeImages && Image != null)
                {
                    lines += (int)((ImageHeight + ImageHeight % lineHeight) / lineHeight);
                }

                return lines;
            }
        }

        private LinkedList<Block> _blocks = new LinkedList<Block>();
        private double _lineHeight;
        private LinkedListNode<Block> _bottomBlock, _curBlock;
        private int _curLine;
        private bool _isProcessingText;

        private Typeface Typeface
        {
            get
            {
                return new Typeface(this.FontFamily, this.FontStyle, this.FontWeight, this.FontStretch);
            }
        }

        private Color BackgroundColor
        {
            get
            {
                if (this.Background is SolidColorBrush)
                {
                    return ((SolidColorBrush)this.Background).Color;
                }
                return Colors.Black;
            }
        }

        private string FormatNick(string nick)
        {
            if (!this.UseTabularView)
            {
                if (nick == null)
                {
                    nick = "* ";
                }
                else
                {
                    nick = string.Format("<{0}> ", nick);
                }
            }
            return nick ?? "*";
        }

        private string FormatTime(DateTime time)
        {
            return this.ShowTimestamp ? time.ToString(this.TimestampFormat + " ") : "";
        }

        private Brush GetNickColor(int hashCode)
        {
            var rand = new Random(hashCode * (this.NicknameColorSeed + 1));
            float bgv = (float)Math.Max(Math.Max(this.BackgroundColor.R, this.BackgroundColor.G), this.BackgroundColor.B) / 255f;

            float v = (float)rand.NextDouble() * NickBrightnessBand + (bgv < 0.5f ? (1f - NickBrightnessBand) : MinNickBrightness);
            float h = 360f * (float)rand.NextDouble();
            float s = .4f + (.6f * (float)rand.NextDouble());
            return new SolidColorBrush(new HsvColor(1f, h, s, v).ToColor());
        }

        public void AppendBulkLines(IEnumerable<ChatLine> lines)
        {
            foreach (var line in lines)
            {
                var b = new Block();
                b.Source = line;
                b.TimeString = this.FormatTime(b.Source.Time);
                b.NickString = this.FormatNick(b.Source.Nick);

                var offset = _blocks.Last != null ? _blocks.Last.Value.CharEnd : 0;
                b.CharStart = offset;
                offset += b.TimeString.Length + b.NickString.Length + b.Source.Text.Length;
                b.CharEnd = offset;

                _blocks.AddLast(b);
            }
            this.StartProcessingText();
        }

        public void AppendLine(ChatLine line)
        {
            var b = new Block();
            b.Source = line;

            b.TimeString = this.FormatTime(b.Source.Time);
            b.NickString = this.FormatNick(b.Source.Nick);

            var offset = _blocks.Last != null ? _blocks.Last.Value.CharEnd : 0;
            b.CharStart = offset;
            offset += b.TimeString.Length + b.NickString.Length + b.Source.Text.Length;
            b.CharEnd = offset;

            _blocks.AddLast(b);
            this.FormatOne(b, this.AutoSizeColumn);
            _bufferLines += b.GetTotalLines(_lineHeight, true);

            while (_blocks.Count > this.BufferLines)
            {
                if (_blocks.First.Value.Text != null)
                {
                    _bufferLines -= _blocks.First.Value.GetTotalLines(_lineHeight, true);
                }
                if (_blocks.First == _curSearchBlock)
                {
                    this.ClearSearch();
                }
                _blocks.RemoveFirst();
            }

            this.InvalidateScrollInfo();
            if (!_isAutoScrolling || _isSelecting)
            {
                _scrollPos += b.GetTotalLines(_lineHeight, true);
            }
            this.InvalidateVisual();
        }

        private void FormatOne(Block b, bool autoSize)
        {
            b.Foreground = this.Palette[b.Source.ColorKey];

            var formatter = new ChatFormatter(this.Typeface, this.FontSize, this.Foreground, this.Palette);
            b.Time = formatter.Format(b.TimeString, null, this.ViewportWidth, b.Foreground, this.Background,
                TextWrapping.NoWrap).FirstOrDefault();
            b.NickX = b.Time != null ? b.Time.WidthIncludingTrailingWhitespace : 0.0;

            var nickBrush = b.Foreground;
            if (this.ColorizeNicknames && b.Source.NickHashCode != 0)
            {
                nickBrush = this.GetNickColor(b.Source.NickHashCode);
            }
            b.Nick = formatter.Format(b.NickString, null, this.ViewportWidth - b.NickX, nickBrush, this.Background,
                TextWrapping.NoWrap).First();
            b.TextX = b.NickX + b.Nick.WidthIncludingTrailingWhitespace;

            if (autoSize && b.TextX > this.ColumnWidth)
            {
                this.ColumnWidth = b.TextX;
                this.InvalidateAll(false);
            }

            if (this.UseTabularView)
            {
                b.TextX = this.ColumnWidth + SeparatorPadding * 2.0 + 1.0;
                b.NickX = this.ColumnWidth - b.Nick.WidthIncludingTrailingWhitespace;
            }

            b.Text = formatter.Format(b.Source.Text, b.Source, this.ViewportWidth - b.TextX, b.Foreground,
                this.Background, TextWrapping.Wrap).ToArray();

            var imageMatches = Regex.Matches(b.Source.Text, @"(https?://[^\s?]+\.(jpg|gif|png)(\?[^\s]+)?)");
            if (imageMatches.Count > 0)
            {
                var bmpImg = new BitmapImage();
                bmpImg.BeginInit();
                bmpImg.UriSource = new Uri(imageMatches[0].Value, UriKind.Absolute);
                bmpImg.EndInit();

                if (bmpImg.IsDownloading)
                {
                    bmpImg.DownloadCompleted += (e, arg) =>
                    {
                        OnBitmapDownloadComplete(b, bmpImg);
                    };
                }
                else
                {
                    OnBitmapDownloadComplete(b, bmpImg);
                }
            }
        }

        private void OnBitmapDownloadComplete(Block block, BitmapImage bmpImg)
        {
            if (bmpImg.Height > 1)
            {
                block.Image = bmpImg;
                block.ImageWidth = bmpImg.Width;
                block.ImageHeight = bmpImg.Height;
            }

            InvalidateAll(false);
        }

        private void InvalidateAll(bool styleChanged)
        {
            var formatter = new ChatFormatter(this.Typeface, this.FontSize, this.Foreground, this.Palette);
            _lineHeight = Math.Ceiling(this.FontSize * this.Typeface.FontFamily.LineSpacing);

            if (styleChanged)
            {
                var offset = 0;
                foreach (var b in _blocks)
                {
                    b.CharStart = offset;
                    b.TimeString = this.FormatTime(b.Source.Time);
                    b.NickString = this.FormatNick(b.Source.Nick);
                    offset += b.TimeString.Length + b.NickString.Length + b.Source.Text.Length;
                    b.CharEnd = offset;
                }
            }

            this.StartProcessingText();
        }

        private void StartProcessingText()
        {
            _curBlock = _blocks.Last;
            _curLine = 0;
            if (!_isProcessingText)
            {
                _isProcessingText = true;
                Application.Current.Dispatcher.BeginInvoke((Action)ProcessText, DispatcherPriority.ApplicationIdle, null);
            }
        }

        private void ProcessText()
        {
            int count = 0;
            while (_curBlock != null && count < TextProcessingBatchSize)
            {
                int oldLineCount = _curBlock.Value.GetTotalLines(_lineHeight, false);

                this.FormatOne(_curBlock.Value, false);

                int newLineCount = _curBlock.Value.GetTotalLines(_lineHeight, false);

                _curLine += newLineCount;
                int deltaLines = newLineCount - oldLineCount;

                _bufferLines += deltaLines;
                if (_curLine < _scrollPos)
                {
                    _scrollPos += deltaLines;
                }
                _curBlock = _curBlock.Previous;
                count++;
            }

            if (_curBlock == null)
            {
                _isProcessingText = false;
            }
            else
            {
                Application.Current.Dispatcher.BeginInvoke((Action)ProcessText, DispatcherPriority.ApplicationIdle, null);
            }

            this.InvalidateScrollInfo();
            this.InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            var visual = PresentationSource.FromVisual(this);
            if (visual == null)
            {
                return;
            }
            var m = visual.CompositionTarget.TransformToDevice;
            var scaledPen = new Pen(this.DividerBrush, 1 / m.M11);
            double guidelineHeight = scaledPen.Thickness;

            double vPos = this.ActualHeight;
            int curLine = 0;
            _bottomBlock = null;
            var guidelines = new GuidelineSet();

            dc.DrawRectangle(Brushes.Transparent, null, new Rect(new Size(this.ViewportWidth, this.ActualHeight)));

            var node = _blocks.Last;
            do
            {
                if (node == null)
                {
                    break;
                }

                var block = node.Value;
                block.Y = double.NaN;

                bool drawAny = false;
                if (block.Text == null || block.Text.Length < 1)
                {
                    continue;
                }

                var imageLineCount = block.GetTotalLines(_lineHeight, true) - block.Text.Length;
                for (int j = imageLineCount - 1; j >= 0; j--)
                {
                    if (curLine++ < _scrollPos)
                    {
                        continue;
                    }
                    vPos -= _lineHeight;
                    drawAny = true;
                }

                for (int j = block.Text.Length - 1; j >= 0; --j)
                {
                    var line = block.Text[j];
                    if (curLine++ < _scrollPos)
                    {
                        continue;
                    }
                    vPos -= line.Height;
                    drawAny = true;
                }
                
                if (drawAny)
                {
                    block.Y = vPos;

                    if ((block.Source.Marker & ChatMarker.NewMarker) > 0)
                    {
                        var markerBrush = new LinearGradientBrush(this.NewMarkerColor,
                            this.NewMarkerTransparentColor, 90.0);
                        dc.DrawRectangle(markerBrush, null,
                            new Rect(new Point(0.0, block.Y), new Size(this.ViewportWidth, _lineHeight * 5)));
                    }
                    if ((block.Source.Marker & ChatMarker.OldMarker) > 0)
                    {
                        var blockHeight = block.GetTotalHeight(_lineHeight);
                        var markerBrush = new LinearGradientBrush(this.OldMarkerTransparentColor,
                            this.OldMarkerColor, 90.0);
                        dc.DrawRectangle(markerBrush, null,
                            new Rect(new Point(0.0, (block.Y + blockHeight) - _lineHeight * 5),
                                new Size(this.ViewportWidth, _lineHeight * 5)));
                    }

                    if (_bottomBlock == null)
                    {
                        _bottomBlock = node;
                    }

                    guidelines.GuidelinesY.Add(vPos + guidelineHeight);
                }
            }
            while (node.Previous != null && vPos >= -_lineHeight * 5.0 && (node = node.Previous) != null);

            dc.PushGuidelineSet(guidelines);

            if (this.UseTabularView)
            {
                double lineX = this.ColumnWidth + SeparatorPadding;
                dc.DrawLine(scaledPen, new Point(lineX, 0.0), new Point(lineX, this.ActualHeight));
            }

            if (_blocks.Count < 1)
            {
                return;
            }

            do
            {
                var block = node.Value;
                if (double.IsNaN(block.Y))
                {
                    continue;
                }

                if ((block.Source.Marker & ChatMarker.Attention) > 0)
                {
                    var blockHeight = block.GetTotalHeight(_lineHeight);
                    var markerBrush = new SolidColorBrush(this.AttentionColor);
                    dc.DrawRectangle(markerBrush, null,
                        new Rect(new Point(block.TextX, block.Y), new Size(this.ViewportWidth - block.TextX, blockHeight)));
                }

                block.Nick.Draw(dc, new Point(block.NickX, block.Y), InvertAxes.None);
                if (block.Time != null)
                {
                    block.Time.Draw(dc, new Point(0.0, block.Y), InvertAxes.None);
                }

                for (int k = 0; k < block.Text.Length; k++)
                {
                    block.Text[k].Draw(dc, new Point(block.TextX, block.Y + k * _lineHeight), InvertAxes.None);
                }

                if (block.Image != null)
                {
                    dc.DrawImage(block.Image, new Rect(block.TextX, block.Y + block.Text.Length * _lineHeight, block.ImageWidth, block.ImageHeight));
                }

                if (this.IsSelecting)
                {
                    this.DrawSelectionHighlight(dc, block);
                }
                if (node == _curSearchBlock)
                {
                    this.DrawSearchHighlight(dc, node.Value);
                }
            }
            while ((node = node.Next) != null);
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            if (sizeInfo.WidthChanged)
            {
                this.InvalidateAll(false);
            }
        }
    }
}
