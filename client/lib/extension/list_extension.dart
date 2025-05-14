import 'package:flutter/material.dart';

extension ListExtension on List<Widget> {
  List<Widget> alternateWidgets(Widget extra) {
    final temp = <Widget>[];

    var shouldAdd = false;
    for (var element in this) {
      if (shouldAdd) {
        temp.add(extra);
      }

      temp.add(element);

      shouldAdd = true;
    }

    return temp;
  }
}
