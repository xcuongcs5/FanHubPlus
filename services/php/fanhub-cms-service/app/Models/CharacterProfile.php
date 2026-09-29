<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Factories\HasFactory;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Support\Str;

class CharacterProfile extends Model
{
    use HasFactory;

    protected $table = 'cms_character_profiles';

    protected $keyType = 'string';
    public $incrementing = false;

    protected $fillable = [
        'id',
        'category_id',
        'name',
        'biography',
        'avatar_url',
    ];

    protected static function booted(): void
    {
        static::creating(function (CharacterProfile $profile) {
            if (empty($profile->id)) {
                $profile->id = 'chr_' . Str::lower(Str::random(12));
            }
        });
    }

    /**
     * Danh mục thuộc về
     */
    public function category(): BelongsTo
    {
        return $this->belongsTo(Category::class, 'category_id');
    }
}
