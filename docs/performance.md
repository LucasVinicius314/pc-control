# Performance (a small task manager)

How busy the PC is, and which programs are using it. It is on the phone (the **PC** button, then the
**Performance** tab) and on the controller UI (the **Performance** screen).

## What is shown

* **CPU**: total use, as Task Manager's "Utility" figure, with the thread count.
* **GPU**: the busiest GPU engine's use (3D, video decode, copy and so on), like Task Manager. Left
  out when Windows gives no GPU numbers.
* **Memory**: used / total RAM.
* **Disk**: active time (100% minus idle time), and the read and write speed of all disks.
* **Network**: download and upload speed of the active network adapters.
* **Programs**: every running program is listed (some of Windows' own are protected from being measured
  without administrator rights; they still show, with the figures that can be read, and the rest as 0).
  Every program with the same name is added together ("chrome ×12"). Columns are CPU,
  memory (working set), GPU and Disk. Sort by any column: on the phone with the buttons above the list,
  in the controller UI with **X**.

## Things to know

* The Disk column of a program is its **I/O**: what it reads and writes, files and network together.
  Windows counts them as one per program. Per-program network speed needs administrator rights, so
  it is not shown.
* Numbers are measured over the time since the previous reading, once a second at most, however many
  screens are open. Nothing is measured while nobody is looking.
* Process numbers are never sent. Programs are shown by name only.
* `GET /api/performance?sort=cpu|memory|gpu|disk&count=25` (see the protocol notes). The capability
  is `performance`.
