# MVC Paradigm

## Controller

- Class inherits from `Controller`, contains **actions** (methods)
- Action: name, async/sync, annotation, return type (`IActionResult`), parameters, `return View(model)`
- Annotations: `[HttpGet]`, `[HttpPost]`, `[AllowAnonymous]`, etc.

## View

- `.cshtml` file – combination of HTML and Razor C# statements
- `@model` directive at the top defines the model type (strongly typed)
- `@Model.Property` outputs data from the model
- `ViewData` – dictionary, not strongly typed (inferior)
- Logic in view: only `if` / `foreach` / TagHelpers – nothing more complex

## ViewModel

- Helper class with customized data for the view
- Difference: Model = database; ViewModel = customized data for display

## Action URL Parameters

- Query string parameters are automatically mapped to action parameters
- Example: `/Home/About?lang=en` → `About(string lang)`

---

# HTML Basics

## Container Elements

- `<html>`, `<head>`, `<body>` – page structure
- `<div>` – block element (takes up entire line)
- `<span>` – inline element (takes only needed width)
- `<table>`, `<th>`, `<tr>`, `<td>` – tabular display

## Input Elements

- `<form method="post">` – wrapper for input elements, sends POST
- `<input type="text">`, `<input type="submit">`
- `<select>` – dropdown menu
- `<textarea>` – multi-line text input

---

# Twitter Bootstrap

## Grid System

- UI divided into a grid of columns (12-column grid)
- Automatically responsive – mobile, tablet, desktop
- Classes: `container`, `row`, `col-md-X`, etc.

## Modal

- Popup window for displaying information
- Can be opened using HTML attributes or JavaScript functions

# Design

## Colors
- always choose darker colors

## Sytle
- make pages look modern and clear
- create simpler styles