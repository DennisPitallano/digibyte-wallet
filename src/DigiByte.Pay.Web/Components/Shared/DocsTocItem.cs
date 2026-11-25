namespace DigiByte.Pay.Web.Components.Shared;

/// <summary>One entry of a docs page's "On this page" list. <paramref name="Id"/> is the target element's id.</summary>
public sealed record DocsTocItem(string Id, string Label, bool Sub = false);
