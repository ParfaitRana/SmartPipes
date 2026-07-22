using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SmartPipes.Models;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace SmartPipes.Menus;

internal sealed class PortConfigMenu : IClickableMenu
{
    private const int Margin = 32;
    private const int Gap = 8;
    private const int ButtonHeight = 40;
    private const int InventorySlotSize = 56;

    private static readonly Rectangle DropDownBackgroundSource = new(433, 451, 3, 3);
    private static readonly Rectangle DropDownButtonSource = new(437, 450, 10, 11);
    private static readonly Rectangle NativeButtonSource = new(432, 439, 9, 9);

    private static readonly int[] Qualities = { 0, 1, 2, 4 };

    private readonly PortSettings settings;
    private readonly bool shippingValve;
    private readonly string targetName;
    private readonly ITranslationHelper translation;
    private readonly Action<PortSettings> save;
    private readonly TextBox filterInput;
    private bool lastFilterDestinationAllow = true;
    private bool inventoryPickerOpen;
    private bool modeDropdownOpen;
    private SliderDragTarget sliderDragTarget;
    private bool sliderHasDeferredChanges;
    private string? pickedFilterId;
    private string? pickedFilterName;
    private int allowPage;
    private int denyPage;
    private Item? hoveredItem;

    public PortConfigMenu(
        PortSettings settings,
        bool shippingValve,
        string targetName,
        ITranslationHelper translation,
        Action<PortSettings> save)
        : base(
            (Game1.uiViewport.Width - Math.Min(1000, Game1.uiViewport.Width - 80)) / 2,
            (Game1.uiViewport.Height - Math.Min(630, Game1.uiViewport.Height - 80)) / 2,
            Math.Min(1000, Game1.uiViewport.Width - 80),
            Math.Min(630, Game1.uiViewport.Height - 80),
            showUpperRightCloseButton: true)
    {
        this.settings = settings;
        this.shippingValve = shippingValve;
        this.targetName = targetName;
        this.translation = translation;
        this.save = save;
        this.filterInput = new TextBox(Game1.content.Load<Texture2D>("LooseSprites\\textBox"), Game1.mouseCursors, Game1.smallFont, Game1.textColor)
        {
            Width = 380,
            Text = string.Empty
        };
        this.RepositionInput();
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (this.upperRightCloseButton?.containsPoint(x, y) == true)
        {
            this.exitThisMenu(playSound);
            return;
        }

        if (this.inventoryPickerOpen)
        {
            for (int index = 0; index < Game1.player.Items.Count; index++)
            {
                if (!this.InventorySlot(index).Contains(x, y) || Game1.player.Items[index] is not Item item)
                    continue;

                this.pickedFilterId = item.QualifiedItemId;
                this.pickedFilterName = item.DisplayName;
                this.filterInput.Text = item.DisplayName;
                this.inventoryPickerOpen = false;
                Game1.playSound("coin");
                return;
            }

            if (!this.InventoryPickerPanel.Contains(x, y))
            {
                this.inventoryPickerOpen = false;
                Game1.playSound("bigDeSelect");
            }
            return;
        }

        for (int index = 0; index < Math.Min(12, Game1.player.Items.Count); index++)
        {
            if (!this.HotbarSlot(index).Contains(x, y) || Game1.player.Items[index] is not Item item)
                continue;

            this.pickedFilterId = item.QualifiedItemId;
            this.pickedFilterName = item.DisplayName;
            this.filterInput.Text = item.DisplayName;
            Game1.playSound("coin");
            return;
        }

        IReadOnlyList<PortMode> modes = this.GetModes();
        if (this.modeDropdownOpen)
        {
            for (int index = 0; index < modes.Count; index++)
            {
                if (!this.ModeOption(index).Contains(x, y))
                    continue;

                this.settings.Mode = modes[index];
                this.modeDropdownOpen = false;
                this.SaveAndSound();
                return;
            }

            this.modeDropdownOpen = false;
        }
        if (this.ModeDropdown.Contains(x, y))
        {
            this.modeDropdownOpen = !this.modeDropdownOpen;
            Game1.playSound("smallSelect");
            return;
        }

        if (this.InputBounds.Contains(x, y))
        {
            this.SelectTextBox(this.filterInput);
            return;
        }
        this.DeselectTextBoxes();

        if (this.QualityRangeSlider.Contains(x, y))
        {
            int minimumX = this.QualityStopX(this.settings.MinimumQuality);
            int maximumX = this.QualityStopX(this.settings.MaximumQuality);
            this.sliderDragTarget = minimumX == maximumX
                ? x < minimumX ? SliderDragTarget.QualityMinimum : SliderDragTarget.QualityMaximum
                : Math.Abs(x - minimumX) <= Math.Abs(x - maximumX)
                    ? SliderDragTarget.QualityMinimum
                    : SliderDragTarget.QualityMaximum;
            this.UpdateQualityFromPointer(x, this.sliderDragTarget == SliderDragTarget.QualityMinimum, persistImmediately: true);
            return;
        }
        for (int column = 0; column < 3; column++)
        {
            if (!this.NumericSliderBounds(column).Contains(x, y))
                continue;

            this.sliderDragTarget = (SliderDragTarget)((int)SliderDragTarget.Priority + column);
            this.UpdateNumericSliderFromPointer(column, x, persistImmediately: true);
            return;
        }

        if (this.AllowSelector.Contains(x, y))
        {
            this.lastFilterDestinationAllow = true;
            this.AddFilter(this.filterInput.Text, allow: true);
            return;
        }
        if (this.DenySelector.Contains(x, y))
        {
            this.lastFilterDestinationAllow = false;
            this.AddFilter(this.filterInput.Text, allow: false);
            return;
        }
        if (this.FullBackpackButton.Contains(x, y))
        {
            this.inventoryPickerOpen = true;
            Game1.playSound("smallSelect");
            return;
        }
        if (this.ClearAllowButton.Contains(x, y))
        {
            this.settings.Allow.Clear();
            this.allowPage = 0;
            this.SaveAndSound();
            return;
        }
        if (this.ClearDenyButton.Contains(x, y))
        {
            this.settings.Deny.Clear();
            this.denyPage = 0;
            this.SaveAndSound();
            return;
        }

        if (this.TryChangePage(x, y, allow: true) || this.TryChangePage(x, y, allow: false))
            return;
        if (this.TryRemoveFilter(x, y, allow: true) || this.TryRemoveFilter(x, y, allow: false))
            return;

    }

    public override void leftClickHeld(int x, int y)
    {
        if (this.sliderDragTarget == SliderDragTarget.QualityMinimum)
            this.UpdateQualityFromPointer(x, minimum: true, persistImmediately: false);
        else if (this.sliderDragTarget == SliderDragTarget.QualityMaximum)
            this.UpdateQualityFromPointer(x, minimum: false, persistImmediately: false);
        else if (this.sliderDragTarget is >= SliderDragTarget.Priority and <= SliderDragTarget.Stock)
            this.UpdateNumericSliderFromPointer((int)this.sliderDragTarget - (int)SliderDragTarget.Priority, x, persistImmediately: false);
    }

    public override void releaseLeftClick(int x, int y)
    {
        this.FlushDeferredSliderChanges();
        this.sliderDragTarget = SliderDragTarget.None;
        base.releaseLeftClick(x, y);
    }

    public override void receiveKeyPress(Keys key)
    {
        if (key == Keys.Escape && this.inventoryPickerOpen)
        {
            this.inventoryPickerOpen = false;
            Game1.playSound("bigDeSelect");
            return;
        }

        if (key == Keys.Escape && this.filterInput.Selected)
        {
            this.DeselectTextBoxes();
            Game1.playSound("bigDeSelect");
            return;
        }

        if (key == Keys.Enter && this.filterInput.Selected)
        {
            this.AddFilter(this.filterInput.Text, this.lastFilterDestinationAllow);
            return;
        }
        if (this.filterInput.Selected)
            return;

        base.receiveKeyPress(key);
    }

    public override void performHoverAction(int x, int y)
    {
        base.performHoverAction(x, y);
        this.hoveredItem = null;
        int count = this.inventoryPickerOpen ? Game1.player.Items.Count : Math.Min(12, Game1.player.Items.Count);
        for (int index = 0; index < count; index++)
        {
            Rectangle bounds = this.inventoryPickerOpen ? this.InventorySlot(index) : this.HotbarSlot(index);
            if (bounds.Contains(x, y) && Game1.player.Items[index] is Item item)
            {
                this.hoveredItem = item;
                break;
            }
        }
    }

    protected override void cleanupBeforeExit()
    {
        this.FlushDeferredSliderChanges();
        this.DeselectTextBoxes();
        base.cleanupBeforeExit();
    }

    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
    {
        base.gameWindowSizeChanged(oldBounds, newBounds);
        this.RepositionInput();
    }

    public override void draw(SpriteBatch b)
    {
        b.Draw(Game1.staminaRect, new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height), Color.Black * 0.35f);
        IClickableMenu.drawTextureBox(b, this.xPositionOnScreen, this.yPositionOnScreen, this.width, this.height, Color.White);

        string title = this.translation.Get(this.shippingValve ? "ui.title.shipping" : "ui.title.port");
        Utility.drawTextWithShadow(b, title, Game1.dialogueFont, new Vector2(this.xPositionOnScreen + Margin, this.yPositionOnScreen + 16), Game1.textColor);
        Vector2 targetSize = Game1.smallFont.MeasureString(this.targetName);
        Utility.drawTextWithShadow(
            b,
            this.targetName,
            Game1.smallFont,
            new Vector2(this.xPositionOnScreen + this.width - Margin - targetSize.X, this.yPositionOnScreen + 31),
            Color.DimGray);

        this.DrawModes(b);
        this.DrawNumbers(b);
        this.DrawQualities(b);
        this.DrawFilterControls(b);
        this.DrawFilterList(b, allow: true);
        this.DrawFilterList(b, allow: false);
        this.DrawPickerLink(b);
        this.DrawHotbar(b);

        if (this.modeDropdownOpen)
            this.DrawModeOptions(b);
        if (this.inventoryPickerOpen)
            this.DrawInventoryPicker(b);

        this.upperRightCloseButton?.draw(b);
        this.drawMouse(b);
        if (this.hoveredItem is not null)
            IClickableMenu.drawToolTip(b, this.hoveredItem.getDescription(), this.hoveredItem.DisplayName, this.hoveredItem);
    }

    private Rectangle InputBounds => new(this.filterInput.X, this.filterInput.Y, this.filterInput.Width, 40);

    private Rectangle AllowSelector => new(this.filterInput.X + this.filterInput.Width + Gap, this.filterInput.Y, 140, ButtonHeight);

    private Rectangle DenySelector => new(this.AllowSelector.Right + Gap, this.filterInput.Y, 140, ButtonHeight);

    private int ModeY => this.yPositionOnScreen + 76;

    private int QualityY => this.yPositionOnScreen + 134;

    private int NumberY => this.yPositionOnScreen + 194;

    private int FilterControlsY => this.yPositionOnScreen + 266;

    private int FilterListY => this.yPositionOnScreen + 318;

    private int FooterY => this.yPositionOnScreen + this.height - 42;

    private Rectangle InventoryPickerLink => new(this.xPositionOnScreen + Margin, this.FooterY, this.ContentWidth - 208, 28);

    private Rectangle FullBackpackButton => new(this.xPositionOnScreen + this.width - Margin - 184, this.FooterY - 6, 184, 40);

    private int ContentWidth => this.width - Margin * 2;

    private Rectangle ModeDropdown => new(this.xPositionOnScreen + Margin, this.ModeY, 350, 44);

    private Rectangle ModeOption(int index) => new(this.ModeDropdown.X, this.ModeDropdown.Bottom + index * ButtonHeight, this.ModeDropdown.Width, ButtonHeight);

    private Rectangle NumberPanel(int column)
    {
        int panelWidth = (this.ContentWidth - Gap * 2) / 3;
        return new Rectangle(this.xPositionOnScreen + Margin + column * (panelWidth + Gap), this.NumberY, panelWidth, ButtonHeight);
    }

    private Rectangle NumericSliderBounds(int column)
    {
        Rectangle panel = this.NumberPanel(column);
        return new Rectangle(panel.X, panel.Y + 24, panel.Width, 28);
    }

    private Rectangle NumericTrack(int column)
    {
        Rectangle slider = this.NumericSliderBounds(column);
        return new Rectangle(slider.X + 8, slider.Center.Y - 5, slider.Width - 92, 10);
    }

    private Rectangle QualityRangeSlider => new(this.xPositionOnScreen + Margin, this.QualityY, 590, 38);

    private Rectangle QualityTrack => new(this.QualityRangeSlider.X + 12, this.QualityRangeSlider.Center.Y - 6, this.QualityRangeSlider.Width - 24, 12);

    private int QualityStopX(int quality)
    {
        int index = Array.IndexOf(Qualities, quality);
        return this.QualityTrack.Left + (int)Math.Round(index * this.QualityTrack.Width / (double)(Qualities.Length - 1));
    }

    private Rectangle FilterPanel(bool allow)
    {
        int panelWidth = (this.ContentWidth - Gap) / 2;
        return new Rectangle(
            this.xPositionOnScreen + Margin + (allow ? 0 : panelWidth + Gap),
            this.FilterListY,
            panelWidth,
            Math.Max(54, this.FooterY - this.FilterListY - 10));
    }

    private Rectangle ClearAllowButton => this.HeaderActionButton(allow: true);

    private Rectangle ClearDenyButton => this.HeaderActionButton(allow: false);

    private Rectangle HeaderActionButton(bool allow)
    {
        Rectangle panel = this.FilterPanel(allow);
        return new Rectangle(panel.Right - 104, panel.Y + 5, 94, 36);
    }

    private Rectangle PageButton(bool allow, bool next)
    {
        Rectangle panel = this.FilterPanel(allow);
        int x = panel.Right + (next ? -174 : -292);
        return new Rectangle(x, panel.Y + 5, 62, 36);
    }

    private Rectangle PageIndicator(bool allow)
    {
        Rectangle panel = this.FilterPanel(allow);
        return new Rectangle(panel.Right - 226, panel.Y + 5, 48, 36);
    }

    private Rectangle FilterEntry(bool allow, int visibleIndex)
    {
        Rectangle panel = this.FilterPanel(allow);
        return new Rectangle(panel.X + 10, panel.Y + 46 + visibleIndex * 42, panel.Width - 20, 38);
    }

    private int VisibleFilterRows => Math.Max(1, (this.FilterPanel(true).Height - 52) / 42);

    private Rectangle InventorySlot(int index)
    {
        int column = index % 12;
        int row = index / 12;
        return new Rectangle(
            this.InventoryPickerPanel.X + 16 + column * InventorySlotSize,
            this.InventoryPickerPanel.Y + 54 + row * InventorySlotSize,
            InventorySlotSize,
            InventorySlotSize);
    }

    private Rectangle InventoryPickerPanel => new(
        this.xPositionOnScreen + (this.width - 12 * InventorySlotSize - 32) / 2,
        this.yPositionOnScreen + (this.height - 238) / 2,
        12 * InventorySlotSize + 32,
        238);

    private Rectangle HotbarSlot(int index)
    {
        int startX = this.xPositionOnScreen + (this.width - 12 * InventorySlotSize) / 2;
        int y = Math.Min(this.yPositionOnScreen + this.height + 12, Game1.uiViewport.Height - InventorySlotSize - 12);
        return new Rectangle(startX + index * InventorySlotSize, y, InventorySlotSize, InventorySlotSize);
    }

    private IReadOnlyList<PortMode> GetModes()
    {
        return this.shippingValve
            ? new[] { PortMode.Input, PortMode.Disabled }
            : new[] { PortMode.Input, PortMode.Output, PortMode.Both, PortMode.Disabled };
    }

    private void UpdateQualityFromPointer(int x, bool minimum, bool persistImmediately)
    {
        Rectangle track = this.QualityTrack;
        double position = Math.Clamp((x - track.Left) / (double)track.Width, 0d, 1d);
        int index = Math.Clamp((int)Math.Round(position * (Qualities.Length - 1)), 0, Qualities.Length - 1);
        int quality = Qualities[index];
        int oldMinimum = this.settings.MinimumQuality;
        int oldMaximum = this.settings.MaximumQuality;
        if (minimum)
        {
            this.settings.MinimumQuality = Math.Min(quality, this.settings.MaximumQuality);
        }
        else
        {
            this.settings.MaximumQuality = Math.Max(quality, this.settings.MinimumQuality);
        }

        if (oldMinimum != this.settings.MinimumQuality || oldMaximum != this.settings.MaximumQuality)
            this.CommitSliderChange(persistImmediately);
    }

    private void UpdateNumericSliderFromPointer(int column, int x, bool persistImmediately)
    {
        Rectangle track = this.NumericTrack(column);
        double position = Math.Clamp((x - track.Left) / (double)track.Width, 0d, 1d);
        int oldValue;
        int value;
        switch (column)
        {
            case 0:
                oldValue = this.settings.Priority;
                value = (int)Math.Round((-100 + position * 200) / 10d) * 10;
                this.settings.Priority = value;
                break;
            case 1:
                oldValue = this.settings.MinimumKeep;
                value = (int)Math.Round(position * 100);
                this.settings.MinimumKeep = value;
                break;
            default:
                oldValue = this.settings.MaximumStock;
                value = (int)Math.Round(position * 100);
                this.settings.MaximumStock = value;
                break;
        }

        if (oldValue != value)
            this.CommitSliderChange(persistImmediately);
    }

    private void CommitSliderChange(bool persistImmediately)
    {
        if (persistImmediately)
            this.SaveAndSound();
        else
            this.sliderHasDeferredChanges = true;
    }

    private void FlushDeferredSliderChanges()
    {
        if (!this.sliderHasDeferredChanges)
            return;

        this.save(this.settings);
        this.sliderHasDeferredChanges = false;
    }

    private void AddFilter(string rawValue, bool allow)
    {
        string value = this.pickedFilterId is not null && string.Equals(rawValue.Trim(), this.pickedFilterName, StringComparison.CurrentCultureIgnoreCase)
            ? this.pickedFilterId
            : this.ResolveFilterValue(rawValue);
        if (value.Length == 0)
            return;

        if (allow)
        {
            this.settings.Deny.Remove(value);
            this.settings.Allow.Add(value);
            this.allowPage = Math.Max(0, (this.settings.Allow.Count - 1) / this.VisibleFilterRows);
        }
        else
        {
            this.settings.Allow.Remove(value);
            this.settings.Deny.Add(value);
            this.denyPage = Math.Max(0, (this.settings.Deny.Count - 1) / this.VisibleFilterRows);
        }

        this.filterInput.Text = string.Empty;
        this.pickedFilterId = null;
        this.pickedFilterName = null;
        this.SaveAndSound();
    }

    private bool TryRemoveFilter(int x, int y, bool allow)
    {
        HashSet<string> set = allow ? this.settings.Allow : this.settings.Deny;
        int page = allow ? this.allowPage : this.denyPage;
        string[] entries = set.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        for (int visibleIndex = 0; visibleIndex < this.VisibleFilterRows; visibleIndex++)
        {
            int index = page * this.VisibleFilterRows + visibleIndex;
            if (index >= entries.Length || !this.FilterEntry(allow, visibleIndex).Contains(x, y))
                continue;

            set.Remove(entries[index]);
            int lastPage = Math.Max(0, (set.Count - 1) / this.VisibleFilterRows);
            if (allow)
                this.allowPage = Math.Min(this.allowPage, lastPage);
            else
                this.denyPage = Math.Min(this.denyPage, lastPage);
            this.SaveAndSound();
            return true;
        }
        return false;
    }

    private bool TryChangePage(int x, int y, bool allow)
    {
        int count = allow ? this.settings.Allow.Count : this.settings.Deny.Count;
        int pageCount = Math.Max(1, (count + this.VisibleFilterRows - 1) / this.VisibleFilterRows);
        if (pageCount <= 1)
            return false;

        int page = allow ? this.allowPage : this.denyPage;
        int nextPage = page;
        if (this.PageButton(allow, next: false).Contains(x, y))
            nextPage = Math.Max(0, page - 1);
        else if (this.PageButton(allow, next: true).Contains(x, y))
            nextPage = Math.Min(pageCount - 1, page + 1);
        else
            return false;

        if (allow)
            this.allowPage = nextPage;
        else
            this.denyPage = nextPage;
        Game1.playSound("smallSelect");
        return true;
    }

    private void SaveAndSound()
    {
        this.save(this.settings);
        Game1.playSound("smallSelect");
    }

    private void SelectTextBox(TextBox selected)
    {
        this.filterInput.Selected = ReferenceEquals(this.filterInput, selected);
        Game1.keyboardDispatcher.Subscriber = selected;
    }

    private void DeselectTextBoxes()
    {
        this.filterInput.Selected = false;
        if (ReferenceEquals(Game1.keyboardDispatcher.Subscriber, this.filterInput))
            Game1.keyboardDispatcher.Subscriber = null;
    }

    private void RepositionInput()
    {
        this.filterInput.X = this.xPositionOnScreen + Margin;
        this.filterInput.Y = this.FilterControlsY;
    }

    private void DrawModes(SpriteBatch b)
    {
        this.DrawCenteredText(
            b,
            this.translation.Get("ui.flow-mode"),
            new Rectangle(this.ModeDropdown.Right + 16, this.ModeY, 150, ButtonHeight),
            Game1.smallFont,
            Game1.textColor);
        string current = this.translation.Get($"ui.mode.{this.settings.Mode.ToString().ToLowerInvariant()}");
        this.DrawNativeDropDown(b, this.ModeDropdown, current, drawArrow: true, selected: false);
    }

    private void DrawModeOptions(SpriteBatch b)
    {
        IReadOnlyList<PortMode> modes = this.GetModes();
        Rectangle first = this.ModeOption(0);
        Rectangle listBounds = new(first.X, first.Y, first.Width, modes.Count * first.Height);
        this.DrawPanel(b, listBounds, new Color(218, 127, 0));
        for (int index = 0; index < modes.Count; index++)
        {
            PortMode mode = modes[index];
            Rectangle bounds = this.ModeOption(index);
            if (this.settings.Mode == mode)
                b.Draw(Game1.staminaRect, new Rectangle(bounds.X + 2, bounds.Y, bounds.Width - 4, bounds.Height), new Color(255, 236, 190));
            this.DrawCenteredText(b, this.translation.Get($"ui.mode.{mode.ToString().ToLowerInvariant()}"), bounds, Game1.smallFont, Game1.textColor);
        }
    }

    private void DrawNumbers(SpriteBatch b)
    {
        string[] labels =
        {
            this.translation.Get("ui.priority"),
            this.translation.Get("ui.minimum-keep"),
            this.translation.Get("ui.maximum-stock")
        };
        int[] values = { this.settings.Priority, this.settings.MinimumKeep, this.settings.MaximumStock };
        for (int column = 0; column < 3; column++)
        {
            Rectangle panel = this.NumberPanel(column);
            this.DrawCenteredText(b, labels[column], new Rectangle(panel.X, panel.Y - 4, panel.Width, 26), Game1.smallFont, Game1.textColor);
            Rectangle track = this.NumericTrack(column);
            this.DrawPanel(b, track, new Color(194, 194, 178));
            double position = column switch
            {
                0 => (Math.Clamp(values[column], -100, 100) + 100) / 200d,
                _ => Math.Clamp(values[column], 0, 100) / 100d
            };
            int handleX = track.Left + (int)Math.Round(position * track.Width);
            b.Draw(Game1.staminaRect, new Rectangle(track.Left + 2, track.Y + 2, Math.Max(2, handleX - track.Left), track.Height - 4), new Color(226, 143, 18));
            this.DrawPanel(b, new Rectangle(handleX - 7, track.Center.Y - 11, 14, 22), new Color(226, 143, 18));
            this.DrawCenteredText(b, values[column].ToString(), new Rectangle(track.Right + 8, track.Y - 7, 78, 28), Game1.smallFont, Game1.textColor);
        }
    }

    private void DrawQualities(SpriteBatch b)
    {
        Rectangle track = this.QualityTrack;
        this.DrawPanel(b, track, new Color(194, 194, 178));
        int minimumX = this.QualityStopX(this.settings.MinimumQuality);
        int maximumX = this.QualityStopX(this.settings.MaximumQuality);
        b.Draw(Game1.staminaRect, new Rectangle(minimumX, track.Y + 2, Math.Max(4, maximumX - minimumX), track.Height - 4), new Color(226, 143, 18));
        for (int index = 0; index < Qualities.Length; index++)
        {
            int stopX = this.QualityStopX(Qualities[index]);
            b.Draw(Game1.staminaRect, new Rectangle(stopX - 2, track.Y - 4, 4, track.Height + 8), new Color(111, 76, 48));
        }

        this.DrawPanel(b, new Rectangle(minimumX - 8, track.Center.Y - 13, 16, 26), QualityColor(this.settings.MinimumQuality));
        this.DrawPanel(b, new Rectangle(maximumX - 8, track.Center.Y - 13, 16, 26), QualityColor(this.settings.MaximumQuality));
        string range = this.translation.Get("ui.quality-range", new
        {
            minimum = this.translation.Get($"quality.{this.settings.MinimumQuality}"),
            maximum = this.translation.Get($"quality.{this.settings.MaximumQuality}")
        });
        this.DrawCenteredText(
            b,
            range,
            new Rectangle(track.Right + 20, this.QualityY, this.ContentWidth - this.QualityRangeSlider.Width - 20, 34),
            Game1.smallFont,
            Game1.textColor);
    }

    private void DrawFilterControls(SpriteBatch b)
    {
        this.filterInput.Update();
        this.filterInput.Draw(b);
        this.DrawButton(b, this.AllowSelector, this.translation.Get("ui.add-allow"), this.lastFilterDestinationAllow);
        this.DrawButton(b, this.DenySelector, this.translation.Get("ui.add-deny"), !this.lastFilterDestinationAllow);
    }

    private void DrawFilterList(SpriteBatch b, bool allow)
    {
        Rectangle panel = this.FilterPanel(allow);
        this.DrawPanel(b, panel, new Color(248, 226, 174));
        HashSet<string> set = allow ? this.settings.Allow : this.settings.Deny;
        int page = allow ? this.allowPage : this.denyPage;
        string title = this.translation.Get(allow ? "ui.allow-list" : "ui.deny-list", new { count = set.Count });
        Utility.drawTextWithShadow(b, title, Game1.smallFont, new Vector2(panel.X + 10, panel.Y + 9), Game1.textColor);
        int pageCount = Math.Max(1, (set.Count + this.VisibleFilterRows - 1) / this.VisibleFilterRows);
        if (pageCount > 1)
        {
            this.DrawButton(b, this.PageButton(allow, next: false), this.translation.Get("ui.previous-page"), false);
            this.DrawCenteredText(b, $"{page + 1}/{pageCount}", this.PageIndicator(allow), Game1.smallFont, Game1.textColor);
            this.DrawButton(b, this.PageButton(allow, next: true), this.translation.Get("ui.next-page"), false);
        }
        this.DrawButton(b, this.HeaderActionButton(allow), this.translation.Get("ui.clear"), false);

        string[] entries = set.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        for (int visibleIndex = 0; visibleIndex < this.VisibleFilterRows; visibleIndex++)
        {
            int index = page * this.VisibleFilterRows + visibleIndex;
            if (index >= entries.Length)
                break;

            Rectangle entry = this.FilterEntry(allow, visibleIndex);
            this.DrawPanel(b, entry, allow ? new Color(219, 239, 202) : new Color(244, 205, 194));
            string value = this.TrimToWidth(entries[index], entry.Width - 42);
            Vector2 valueSize = Game1.smallFont.MeasureString(value);
            Utility.drawTextWithShadow(b, value, Game1.smallFont, new Vector2(entry.X + 8, entry.Center.Y - valueSize.Y / 2), Game1.textColor);
            Vector2 removeSize = Game1.smallFont.MeasureString("X");
            Utility.drawTextWithShadow(b, "X", Game1.smallFont, new Vector2(entry.Right - 10 - removeSize.X, entry.Center.Y - removeSize.Y / 2), Color.DarkRed);
        }
    }

    private void DrawPickerLink(SpriteBatch b)
    {
        this.DrawCenteredText(b, this.translation.Get("ui.pick-from-backpack"), this.InventoryPickerLink, Game1.smallFont, Color.DimGray);
        this.DrawButton(b, this.FullBackpackButton, this.translation.Get("ui.full-backpack"), false);
    }

    private void DrawHotbar(SpriteBatch b)
    {
        for (int index = 0; index < Math.Min(12, Game1.player.Items.Count); index++)
        {
            Rectangle slot = this.HotbarSlot(index);
            this.DrawPanel(b, slot, new Color(238, 205, 152));
            if (Game1.player.Items[index] is Item item)
                item.drawInMenu(b, new Vector2(slot.X - 4, slot.Y - 4), 0.75f, 1f, 0.9f, StackDrawType.Hide, Color.White, drawShadow: false);
        }
    }

    private void DrawInventoryPicker(SpriteBatch b)
    {
        Rectangle panel = this.InventoryPickerPanel;
        b.Draw(Game1.staminaRect, new Rectangle(this.xPositionOnScreen + 8, this.yPositionOnScreen + 8, this.width - 16, this.height - 16), Color.Black * 0.55f);
        IClickableMenu.drawTextureBox(b, panel.X, panel.Y, panel.Width, panel.Height, Color.White);
        this.DrawCenteredText(b, this.translation.Get("ui.pick-item-title"), new Rectangle(panel.X + 16, panel.Y + 12, panel.Width - 32, 32), Game1.smallFont, Game1.textColor);
        for (int index = 0; index < Game1.player.Items.Count; index++)
        {
            Rectangle slot = this.InventorySlot(index);
            this.DrawPanel(b, slot, new Color(238, 205, 152));
            if (Game1.player.Items[index] is Item item)
                item.drawInMenu(b, new Vector2(slot.X - 4, slot.Y - 4), 0.75f, 1f, 0.9f, StackDrawType.Hide, Color.White, drawShadow: false);
        }
    }

    private void DrawButton(SpriteBatch b, Rectangle bounds, string text, bool selected)
    {
        IClickableMenu.drawTextureBox(
            b,
            Game1.mouseCursors,
            NativeButtonSource,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            selected ? new Color(255, 225, 150) : Color.White,
            4f,
            drawShadow: false);
        this.DrawCenteredText(b, text, bounds, Game1.smallFont, Game1.textColor);
    }

    private void DrawNativeDropDown(SpriteBatch b, Rectangle bounds, string text, bool drawArrow, bool selected)
    {
        IClickableMenu.drawTextureBox(
            b,
            Game1.mouseCursors,
            DropDownBackgroundSource,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            selected ? new Color(255, 232, 174) : Color.White,
            4f,
            drawShadow: false);
        if (drawArrow)
        {
            b.Draw(
                Game1.mouseCursors,
                new Vector2(bounds.Right - DropDownButtonSource.Width * 4, bounds.Y),
                DropDownButtonSource,
                Color.White,
                0f,
                Vector2.Zero,
                4f,
                SpriteEffects.None,
                1f);
        }

        Rectangle textBounds = new(bounds.X + 8, bounds.Y, bounds.Width - (drawArrow ? 50 : 16), bounds.Height);
        this.DrawCenteredText(b, text, textBounds, Game1.smallFont, Game1.textColor);
    }

    private void DrawPanel(SpriteBatch b, Rectangle bounds, Color fill)
    {
        b.Draw(Game1.staminaRect, bounds, fill);
        b.Draw(Game1.staminaRect, new Rectangle(bounds.X, bounds.Y, bounds.Width, 2), new Color(93, 57, 38));
        b.Draw(Game1.staminaRect, new Rectangle(bounds.X, bounds.Bottom - 2, bounds.Width, 2), new Color(93, 57, 38));
        b.Draw(Game1.staminaRect, new Rectangle(bounds.X, bounds.Y, 2, bounds.Height), new Color(93, 57, 38));
        b.Draw(Game1.staminaRect, new Rectangle(bounds.Right - 2, bounds.Y, 2, bounds.Height), new Color(93, 57, 38));
    }

    private void DrawCenteredText(SpriteBatch b, string text, Rectangle bounds, SpriteFont font, Color color)
    {
        string fitted = this.TrimToWidth(text, bounds.Width - 8, font);
        Vector2 size = font.MeasureString(fitted);
        Utility.drawTextWithShadow(b, fitted, font, new Vector2(bounds.Center.X - size.X / 2, bounds.Center.Y - size.Y / 2), color);
    }

    private string TrimToWidth(string value, float width, SpriteFont? font = null)
    {
        font ??= Game1.smallFont;
        if (font.MeasureString(value).X <= width)
            return value;

        const string suffix = "...";
        while (value.Length > 0 && font.MeasureString(value + suffix).X > width)
            value = value[..^1];
        return value + suffix;
    }

    private static string NormalizeFilterValue(string value)
    {
        value = value.Trim();
        return value.StartsWith("(0)", StringComparison.OrdinalIgnoreCase)
            ? $"(O){value[3..]}"
            : value;
    }

    private string ResolveFilterValue(string rawValue)
    {
        string value = NormalizeFilterValue(rawValue);
        if (value.Length == 0 || value.StartsWith('(') || value.Contains('_'))
            return value;

        foreach (Item item in Game1.player.Items.OfType<Item>())
        {
            if (string.Equals(item.DisplayName, value, StringComparison.CurrentCultureIgnoreCase))
                return item.QualifiedItemId;
        }

        foreach (string itemId in Game1.objectData.Keys)
        {
            Item item = ItemRegistry.Create($"(O){itemId}");
            if (string.Equals(item.DisplayName, value, StringComparison.CurrentCultureIgnoreCase))
                return item.QualifiedItemId;
        }

        return value;
    }

    private static Color QualityColor(int quality)
    {
        return quality switch
        {
            1 => new Color(190, 208, 215),
            2 => new Color(242, 190, 58),
            4 => new Color(166, 86, 210),
            _ => new Color(202, 175, 132)
        };
    }

    private enum SliderDragTarget
    {
        None,
        QualityMinimum,
        QualityMaximum,
        Priority,
        Keep,
        Stock
    }
}
