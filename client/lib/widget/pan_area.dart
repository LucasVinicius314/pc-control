import 'dart:async';

import 'package:flutter/material.dart';

class PanArea extends StatefulWidget {
  const PanArea({
    super.key,
    required this.fps,
    required this.onPan,
    required this.onPrimaryTap,
    required this.onSecondaryTap,
  });

  final int fps;
  final void Function(Offset offset) onPan;
  final void Function() onPrimaryTap;
  final void Function() onSecondaryTap;

  @override
  State<PanArea> createState() => _PanAreaState();
}

class _PanAreaState extends State<PanArea> {
  var _lastPosition = Offset.zero;
  Timer? _timer;

  void _setupTimer() {
    _timer?.cancel();

    if (widget.fps > 0) {
      _timer = Timer.periodic(
        Duration(milliseconds: (1000 / widget.fps).round()),
        (timer) {
          widget.onPan(_lastPosition);
        },
      );
    }
  }

  @override
  void initState() {
    _setupTimer();
    super.initState();
  }

  @override
  void didUpdateWidget(covariant PanArea oldWidget) {
    _setupTimer();
    super.didUpdateWidget(oldWidget);
  }

  @override
  void dispose() {
    _timer?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(
      builder: (context, constraints) {
        return GestureDetector(
          behavior: HitTestBehavior.opaque,
          onPanUpdate: (details) {
            _lastPosition -= details.delta;
          },
          onTapUp: (details) {
            if (details.localPosition.dx >= constraints.maxWidth / 2) {
              widget.onPrimaryTap();
            } else {
              widget.onSecondaryTap();
            }
          },
        );
      },
    );
  }
}
