# FanHub CMS & Community Service

**Stack:** PHP 8.3 / Laravel 11 (Modular Monolith)  
**Database:** MySQL  
**Pattern:** Modular Monolith with 3 Domain Modules

## Modules

| Module | Responsibility |
|--------|---------------|
| `Catalog` | 8 Fandom Categories, Character Profiles, Multimedia metadata, Merchandise showcase |
| `Community` | Bookmarks, 5-Star Ratings, Fan-submitted articles (Admin review flow), Dynamic Feedback |
| `Events` | Convention calendar, Map/GPS event discovery, Venue specs |

## Setup

```bash
composer install
cp .env.example .env
php artisan key:generate
php artisan migrate
php artisan serve --port=8000
```
