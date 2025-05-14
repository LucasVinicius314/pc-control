import 'package:client/screen/control_screen.dart';
import 'package:client/screen/home_screen.dart';
import 'package:flutter/material.dart';

class App extends StatelessWidget {
  const App({
    super.key,
  });

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      debugShowCheckedModeBanner: false,
      routes: {
        HomeScreen.route: (context) => const HomeScreen(),
        ControlScreen.route: (context) => const ControlScreen(),
      },
      title: 'PC Control',
    );
  }
}
